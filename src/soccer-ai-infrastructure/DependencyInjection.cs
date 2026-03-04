using Microsoft.Extensions.DependencyInjection;
using SoccerAi.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
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

        return services;
    }

    private static void InitDatabase(IServiceCollection services, IConfiguration configuration) // Modified signature
    {
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var defaultConn = configuration.GetConnectionString("DefaultConnection");
            options.UseSqlite(defaultConn);
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
            client.Timeout = TimeSpan.FromMinutes(5);
        });
    }
}
