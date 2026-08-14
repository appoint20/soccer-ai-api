using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using SoccerAi.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Polly;
using SoccerAi.Infrastructure.Options;
using SoccerAi.Infrastructure.Persistence;
using SoccerAi.Infrastructure.Services;
using SoccerAi.Infrastructure.MlNet;

namespace SoccerAi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Model constants (typed options; defaults apply when section absent)
        services.Configure<SoccerAi.Application.Options.DixonColesOptions>(
            configuration.GetSection(SoccerAi.Application.Options.DixonColesOptions.SectionName));
        services.Configure<SoccerAi.Application.Options.CalibrationOptions>(
            configuration.GetSection(SoccerAi.Application.Options.CalibrationOptions.SectionName));
        services.Configure<SoccerAi.Application.Options.LeagueTierOptions>(
            configuration.GetSection(SoccerAi.Application.Options.LeagueTierOptions.SectionName));
        services.Configure<SoccerAi.Application.Options.StrategyOptions>(
            configuration.GetSection(SoccerAi.Application.Options.StrategyOptions.SectionName));
        services.Configure<SoccerAi.Application.Options.ConfluenceOptions>(
            configuration.GetSection(SoccerAi.Application.Options.ConfluenceOptions.SectionName));
        services.Configure<SoccerAi.Application.Options.OddsSyncOptions>(
            configuration.GetSection(SoccerAi.Application.Options.OddsSyncOptions.SectionName));
        services.Configure<SoccerAi.Application.Options.HistoricalOddsOptions>(
            configuration.GetSection(SoccerAi.Application.Options.HistoricalOddsOptions.SectionName));

        services.AddPersistence(configuration);
        services.AddExternalApis(configuration);
        
        RegisterAiAnalysisService(services, configuration);

        services.AddServices();
        
        return services;
    }

    private static void AddServices(this IServiceCollection services)
    {
        // Core Analysis & Prediction
        services.AddScoped<IMatchAnalysisService, MatchAnalysisService>();
        services.AddScoped<IMatchDataProvider, MatchDataProvider>();
        services.AddScoped<IAnalysisPrecomputeService, AnalysisPrecomputeService>();
        services.AddScoped<IFixtureSyncService, FixtureSyncService>();
        services.AddScoped<IOddsBackfillService, OddsBackfillService>();
        services.AddScoped<IHistoricalOddsImportService, HistoricalOddsImportService>();
        services.AddScoped<IAiSyncService, AiSyncService>();
        services.AddScoped<ITeamSyncService, TeamSyncService>();
        
        // Mathematical Engines
        services.AddScoped<IProbabilityPipeline, ProbabilityPipeline>();
        services.AddScoped<IDecisionService, DecisionService>(); // Confluence rule engine (no LLM influence)

        services.AddScoped<ILeagueVolatilityService, LeagueVolatilityService>();

        // Machine Learning (training preparation only — no serving integration yet)
        services.AddScoped<IMlTrainingService, MlTrainingService>();
        services.AddSingleton<MlTrainingDataBuilder>();

        // Security & Utilities
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<INlpService, NlpService>();

        // NOTE: background sync now lives in the dedicated soccer-ai-worker
        // service (SyncWorker); the API host runs no sync loops.
    }

    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        // Provider selection via config: "Database:Provider" = "Sqlite" (default) | "Postgres"
        var provider = configuration["Database:Provider"] ?? "Sqlite";

        // The initial Postgres migration (and the SnapshotJson SQLite migration) are
        // hand-written without designer target models, so EF's pending-model-changes
        // heuristic cannot compare models; suppress it for Database.Migrate().
        void ConfigureWarnings(Microsoft.EntityFrameworkCore.DbContextOptionsBuilder options) =>
            options.ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));

        if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = PostgresConnectionString.Normalize(
                configuration.GetConnectionString("PostgresConnection")
                ?? configuration.GetConnectionString("DefaultConnection"));

            services.AddDbContext<PostgresDbContext>(options =>
            {
                options.UseNpgsql(connectionString);
                ConfigureWarnings(options);
            });

            // Everything resolving ApplicationDbContext gets the Postgres context.
            services.AddScoped<ApplicationDbContext>(sp => sp.GetRequiredService<PostgresDbContext>());
        }
        else
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite(connectionString);
                ConfigureWarnings(options);
            });
        }

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<ApplicationDbContext>());
    }


    private static void AddExternalApis(this IServiceCollection services, IConfiguration configuration)
    {
        // Quota is per API key — one tracker for the whole process.
        services.AddSingleton<IApiQuotaTracker, ApiQuotaTracker>();

        // Call outcomes, so a run can tell "nothing changed" from "all rejected".
        services.AddSingleton<IApiCallTracker, ApiCallTracker>();

        // Language-model forecasts scored against the pipeline. Registered
        // unconditionally: the service reports IsEnabled=false without a key or
        // models, so the sync step degrades to a no-op instead of failing.
        services.AddOptions<OpenRouterOptions>()
            .Bind(configuration.GetSection(OpenRouterOptions.SectionName));

        services.AddHttpClient(OpenRouterForecastService.HttpClientName, (provider, client) =>
        {
            var opt = provider.GetRequiredService<IOptions<OpenRouterOptions>>().Value;
            var key = string.IsNullOrWhiteSpace(opt.ApiKey)
                ? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
                : opt.ApiKey;

            client.BaseAddress = new Uri(opt.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds);

            if (!string.IsNullOrWhiteSpace(key))
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {key}");

            // OpenRouter attributes usage on its dashboard by these headers.
            client.DefaultRequestHeaders.Add("HTTP-Referer", opt.AppUrl);
            client.DefaultRequestHeaders.Add("X-Title", opt.AppTitle);
        });

        services.AddScoped<IMatchForecastService, OpenRouterForecastService>();

        services.AddHttpClient<IApiFootballService, ApiFootballService>((provider, client) =>
        {
            var options = configuration.GetSection("ApiFootball").Get<FootballApiOptions>();
            var apiKey = Environment.GetEnvironmentVariable("API_FOOTBALL_KEY") ?? options?.ApiKey;

            client.BaseAddress = new Uri(options?.BaseUrl ?? "https://v3.football.api-sports.io");
            client.DefaultRequestHeaders.Add("x-apisports-key", apiKey);
        })
        .AddResilienceHandler("football-api", builder =>
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(2)
            });
            builder.AddTimeout(TimeSpan.FromSeconds(30));
        });

        // football-data.co.uk season files. No API key; a whole season is one
        // file, so the timeout is generous and there is nothing to rate-limit.
        services.AddHttpClient(HistoricalOddsImportService.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.Add("User-Agent", "soccer-ai-api/1.0");
        });
    }

    private static void RegisterAiAnalysisService(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiServiceOptions>(configuration.GetSection("AiService"));

        // The LLM only generates narrative text — it must NEVER be required for
        // the statistical flow (model, calibration, decisions, backtest).
        // Without a key (or with AiService:Enabled=false) a no-op service is used.
        var apiKey = ResolveAiApiKey(configuration);
        var enabled = configuration.GetValue("AiService:Enabled", true) && !string.IsNullOrWhiteSpace(apiKey);

        if (!enabled)
        {
            services.AddScoped<IAiAnalysisService, DisabledAiAnalysisService>();
            return;
        }

        services.AddScoped<IAiAnalysisService, OpenAiAnalysisService>();
        services.AddScoped<ChatClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiServiceOptions>>().Value;
            var key = ResolveAiApiKey(configuration) ?? options.ApiKey;

            var clientOptions = new OpenAI.OpenAIClientOptions { Endpoint = new Uri(options.BaseUrl.TrimEnd('/') + "/") };
            return new ChatClient(options.DefaultModel ?? "glm-4-plus", new System.ClientModel.ApiKeyCredential(key), clientOptions);
        });
    }

    /// <summary>AiService:ApiKey (incl. AiService__ApiKey env) with ZAI_API_KEY fallback.</summary>
    private static string? ResolveAiApiKey(IConfiguration configuration)
    {
        var key = configuration["AiService:ApiKey"];
        return string.IsNullOrWhiteSpace(key)
            ? Environment.GetEnvironmentVariable("ZAI_API_KEY")
            : key;
    }

}
