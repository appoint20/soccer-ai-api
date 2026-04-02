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
        InitGeminiService(services, configuration);

        // Sync services
        services.AddScoped<ITeamSyncService, TeamSyncService>();
        services.AddScoped<IFixtureSyncService, FixtureSyncService>();
        services.AddScoped<IGeminiSyncService, GeminiSyncService>();
        services.AddScoped<IFeatureExtractionService, FeatureExtractionService>();
        services.AddScoped<IMlPredictionService, MlPredictionService>();
        
        // ML.NET native trainer services
        services.AddScoped<SoccerAi.Infrastructure.MlNet.MlTrainingDataBuilder>();
        services.AddScoped<IMlTrainingService, SoccerAi.Infrastructure.MlNet.MlTrainingService>();

        services.AddScoped<IGeminiAnalysisService, GeminiAnalysisService>();
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

    private static void InitDatabase(IServiceCollection services, IConfiguration configuration) // Modified signature
    {
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var defaultConn = configuration.GetConnectionString("DefaultConnection");
            options.UseSqlite(defaultConn);
            
            // Default to no-tracking for read-heavy workloads.
            // Write operations should use explicit .AsTracking() or attach entities.
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
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
    
    private static void InitGeminiService(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));

        services.AddHttpClient<IGeminiAnalysisService, GeminiAnalysisService>((provider, client) =>
        {
            var options = provider
                .GetRequiredService<IOptions<GeminiOptions>>()
                .Value;

            var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
                         ?? options.ApiKey;

            client.BaseAddress = new Uri(options.BaseUrl);
            client.DefaultRequestHeaders.Add("x-apisports-key", apiKey);
        })
        .AddResilienceHandler("gemini-api", builder =>
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 2,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(5),
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Result?.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                    or System.Net.HttpStatusCode.ServiceUnavailable
                    || args.Outcome.Exception is HttpRequestException or TaskCanceledException)
            });
            builder.AddTimeout(TimeSpan.FromMinutes(5));
        });
    }
}
