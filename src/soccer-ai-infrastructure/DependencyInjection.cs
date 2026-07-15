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
        services.AddScoped<IAiSyncService, AiSyncService>();
        services.AddScoped<ITeamSyncService, TeamSyncService>();
        
        // Mathematical Engines
        services.AddScoped<IProbabilityPipeline, ProbabilityPipeline>();
        services.AddScoped<DecisionService>(); // Register concretely for AiDecisionService to use
        services.AddScoped<IDecisionService, AiDecisionService>(); // AI-driven implementation

        services.AddScoped<ILeagueAdjustmentService, LeagueAdjustmentService>();
        services.AddScoped<ILeagueVolatilityService, LeagueVolatilityService>();
        services.AddScoped<ITrapDetectionService, TrapDetectionService>();
        services.AddScoped<IFeatureScoringEngine, FeatureScoringEngine>();
        services.AddScoped<IExpectedValueEngine, ExpectedValueEngine>();

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
            var connectionString = NormalizePostgresConnectionString(
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

    /// <summary>
    /// Managed platforms (e.g. Render) inject postgres://user:pass@host:port/db
    /// URLs; Npgsql needs keyword syntax. Pass keyword strings through untouched.
    /// </summary>
    internal static string? NormalizePostgresConnectionString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return raw;

        var uri = new Uri(raw);
        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var database = uri.AbsolutePath.TrimStart('/');
        var port = uri.Port > 0 ? uri.Port : 5432;

        var result =
            $"Host={uri.Host};Port={port};Database={database};Username={user};Password={password}";

        // Managed Postgres almost always requires TLS.
        if (!uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            result += ";SSL Mode=Require";

        return result;
    }

    private static void AddExternalApis(this IServiceCollection services, IConfiguration configuration)
    {
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
    }

    private static void RegisterAiAnalysisService(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiServiceOptions>(configuration.GetSection("AiService"));
        services.AddScoped<IAiAnalysisService, OpenAiAnalysisService>();
        
        services.AddScoped<ChatClient>(sp => 
        {
            var options = sp.GetRequiredService<IOptions<AiServiceOptions>>().Value;
            
            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                throw new InvalidOperationException(
                    "AI Service API Key is missing. Please ensure 'AiService:ApiKey' is configured in appsettings.json " +
                    "or set the 'AiService__ApiKey' environment variable in your hosting environment (e.g., Render).");
            }

            var clientOptions = new OpenAI.OpenAIClientOptions { Endpoint = new Uri(options.BaseUrl.TrimEnd('/') + "/") };
            return new ChatClient(options.DefaultModel ?? "glm-4-plus", new System.ClientModel.ApiKeyCredential(options.ApiKey), clientOptions);
        });
    }

}
