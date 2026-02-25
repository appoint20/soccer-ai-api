using Microsoft.Extensions.DependencyInjection;
using soccer_gpt_application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using soccer_gpt_application.Services;
using soccer_gpt_infrastructure.Options;
using soccer_gpt_infrastructure.Persistence;
using soccer_gpt_infrastructure.Services;
using soccer_gpt_infrastructure.Configuration;

namespace soccer_gpt_infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Database context
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var pgConn = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING") 
                         ?? configuration.GetConnectionString("DefaultConnection");

            if (!string.IsNullOrWhiteSpace(pgConn))
            {
                options.UseNpgsql(pgConn);
            }
            else
            {
                var dbPath = Environment.GetEnvironmentVariable("DB_PATH") ?? "/Users/shivm/Workspace/soccer-gpt-api/soccer.db";
                options.UseSqlite($"Data Source={dbPath};Foreign Keys=True");
            }
        });
        
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        services.AddMemoryCache();

        services.Configure<GeminiOptions>(options =>
        {
            var section = configuration.GetSection(GeminiOptions.SectionName);
            options.ApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") 
                             ?? section["ApiKey"] 
                             ?? string.Empty;
        });

        services.AddHttpClient<IGeminiAnalysisService, GeminiAnalysisService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        InitApiFootballService(services, configuration);

        // Services
        services.AddScoped<IHistoricalDataService, HistoricalDataService>();

        // Sync services
        services.AddScoped<ITeamSyncService, TeamSyncService>();
        services.AddScoped<IFixtureSyncService, FixtureSyncService>();
        services.AddScoped<ISyncJobRunner, SyncJobRunner>();
        
        // ML Prediction service
        services.AddSingleton<IMlPredictionService, MlPredictionService>();
        services.AddScoped<IFeatureExtractionService, FeatureExtractionService>();
        
        // Decision + detection services
        services.AddScoped<IDecisionService, DecisionService>();
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

    private static void InitApiFootballService(IServiceCollection services, IConfiguration configuration)
    {
        var apiConfig = configuration.GetSection("ApiFootball").Get<ApiFootballConfiguration>();
        
        if (apiConfig is null)
            throw new ApplicationException("API-Football configurations are missing!");
        if (string.IsNullOrWhiteSpace(apiConfig.BaseUrl))
            throw new ApplicationException("API-Football BaseUrl is missing.");

        var apiKey = apiConfig.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // Backward compatibility for environments still using a flat env var name.
            apiKey = Environment.GetEnvironmentVariable("APIFOOTBALL_API_KEY");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ApplicationException(
                "API-Football ApiKey is missing. Use ApiFootball__ApiKey (or APIFOOTBALL_API_KEY).");
        
        services.AddHttpClient<IApiFootballService, ApiFootballService>(client =>
        {
            client.BaseAddress = new Uri(apiConfig.BaseUrl);
            client.DefaultRequestHeaders.Add("x-apisports-key", apiKey);
        });
    }
}
