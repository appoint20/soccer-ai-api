using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using SoccerAi.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Polly;
using SoccerAi.Infrastructure.Options;
using SoccerAi.Infrastructure.Persistence;
using SoccerAi.Infrastructure.Services;

namespace SoccerAi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        InitDatabase(services, configuration);
        InitApiFootballService(services, configuration);
        InitAiService(services, configuration);

        // Sync services
        services.AddScoped<ITeamSyncService, TeamSyncService>();
        services.AddScoped<IFixtureSyncService, FixtureSyncService>();
        services.AddScoped<IAiSyncService, AiSyncService>();
        services.AddScoped<IFeatureExtractionService, FeatureExtractionService>();
        services.AddScoped<IMlPredictionService, MlPredictionService>();
        services.AddScoped<IMatchRepository, Repositories.MatchRepository>();
        
        // ML.NET native trainer services
        services.AddScoped<SoccerAi.Infrastructure.MlNet.MlTrainingDataBuilder>();
        services.AddScoped<IMlTrainingService, SoccerAi.Infrastructure.MlNet.MlTrainingService>();

        services.AddScoped<IAiAnalysisService, ZaiAnalysisService>();

        services.AddScoped<IDecisionService, SoccerAi.Infrastructure.Services.DecisionService>();
        services.AddScoped<IMarketCalibrationService, MarketCalibrationServiceImpl>();
        services.AddScoped<IExpectedValueEngine, ExpectedValueEngine>();
        services.AddScoped<ITrapDetectionService, TrapDetectionService>();
        services.AddScoped<IFeatureScoringEngine, FeatureScoringEngine>();
        services.AddScoped<ILeagueAdjustmentService, LeagueAdjustmentService>();
        services.AddSingleton<ILeagueVolatilityService, LeagueVolatilityService>();

        // Analysis pipeline services
        services.AddScoped<IMatchDataProvider, MatchDataProvider>();
        services.AddScoped<IProbabilityPipeline, ProbabilityPipeline>();
        services.AddScoped<IProbabilityConsensusEngine, ProbabilityConsensusEngine>();
        
        // Shared analysis orchestrator (both analysis + combination endpoints)
        services.AddScoped<IMatchAnalysisService, MatchAnalysisService>();

        // Background Schedulers
        services.AddHostedService<DailySyncBackgroundService>();

        return services;
    }

    private static void InitDatabase(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            // --- DEPLOYMENT OVERRIDE ---
            // If DB_PATH is set (Render/Docker), we use it and ensure the directory exists.
            var dbPath = Environment.GetEnvironmentVariable("DB_PATH");
            if (!string.IsNullOrEmpty(dbPath))
            {
                var directory = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                connectionString = $"Data Source={dbPath}";
            }

            options.UseSqlite(connectionString);
        });

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());
        services.AddMemoryCache();
    }

    private static void InitApiFootballService(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FootballApiOptions>(configuration.GetSection(FootballApiOptions.SectionName));

        services.AddHttpClient<IApiFootballService, ApiFootballService>((provider, client) =>
            {
                var options = provider
                    .GetRequiredService<IOptions<FootballApiOptions>>()
                    .Value;

                var apiKey = Environment.GetEnvironmentVariable("FOOTBALL_API_KEY")
                             ?? options.ApiKey;

                client.BaseAddress = new Uri(options.BaseUrl);
                client.DefaultRequestHeaders.Add("x-apisports-key", apiKey);
            })
            .AddResilienceHandler("football-api", builder =>
            {
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Exponential,
                    Delay = TimeSpan.FromSeconds(2),
                    ShouldHandle = args => ValueTask.FromResult(
                        args.Outcome.Result?.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                        or System.Net.HttpStatusCode.ServiceUnavailable
                        or System.Net.HttpStatusCode.GatewayTimeout
                        || args.Outcome.Exception is HttpRequestException or TaskCanceledException)
                });
                builder.AddTimeout(TimeSpan.FromSeconds(30));
            });
    }
    
    private static void InitAiService(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiServiceOptions>(configuration.GetSection("AiService"));

        services.AddHttpClient("ZaiClient", (provider, client) =>
        {
            var options = provider
                .GetRequiredService<IOptions<AiServiceOptions>>()
                .Value;

            var apiKey = Environment.GetEnvironmentVariable("ZAI_API_KEY") 
                         ?? options.ApiKey;

            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            
            client.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        });

        // Register NLP service using the same Z.ai cloud settings
        services.AddHttpClient<INlpService, NlpService>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<AiServiceOptions>>().Value;
            var apiKey = Environment.GetEnvironmentVariable("ZAI_API_KEY") 
                         ?? options.ApiKey;

            client.BaseAddress = new Uri(options.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            
            client.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        });
    }
}
