using Microsoft.Extensions.DependencyInjection;
using soccer_gpt_application.Interfaces;
using soccer_gpt_infrastructure.Services;
using soccer_gpt_infrastructure.Services.Traps;
using soccer_gpt_infrastructure.Repositories;
using soccer_gpt_infrastructure.Services.Sync;
using soccer_gpt_infrastructure.BackgroundServices;

namespace soccer_gpt_infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddHttpClient<IScheduledJobService, ScheduledJobService>();
        services.AddHttpClient<IFootballApiService, FootballApiService>();
        services.AddScoped<ILeagueService, LeagueService>();
        services.AddScoped<ILeaguesRepository, JsonFileLeaguesRepository>();
        services.AddScoped<IFixtureRepository, FixtureService>();
        services.AddScoped<ILocalTeamStatsRepository, JsonFileLocalTeamStatsRepository>();
        services.AddScoped<IPredictionRepository, JsonFilePredictionRepository>();
        services.AddSingleton<IHistoricalDataRepository, ExcelHistoricalDataService>(); // Singleton to cache data check
        services.AddScoped<ITeamStatsService, TeamStatsService>();
        services.AddScoped<IAdvancedStatsService, AdvancedStatsService>();
        
        
        // European Fixtures Service
        services.Configure<EuropeanFixturesOptions>(options =>
        {
            // Options will be populated from configuration
        });
        
        services.AddHttpClient("EuropeanFixturesApi", (sp, client) =>
        {
            var config = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var apiHost = config["EuropeanFixtures:ApiHost"] ?? "free-api-live-football-data.p.rapidapi.com";
            var apiKey = config["EuropeanFixtures:ApiKey"] ?? "";
            
            client.BaseAddress = new Uri($"https://{apiHost}");
            client.DefaultRequestHeaders.Add("x-rapidapi-key", apiKey);
            client.DefaultRequestHeaders.Add("x-rapidapi-host", apiHost);
        });
        
        services.AddScoped<IEuropeanFixturesService, EuropeanFixturesService>();
        
        
        // Trap Detection (Register Service + Strategies)
        services.AddScoped<ITrapDetectionService, TrapDetectionService>();
        services.AddScoped<ITrapDetector, BoreDrawDetector>();
        services.AddScoped<ITrapDetector, OddsTrapDetector>();
        services.AddScoped<ITrapDetector, GoalMarketTrapDetector>();
        services.AddScoped<ITrapDetector, EuropeanFatigueDetector>();
        services.AddScoped<ITrapDetector, DerbyDetector>();
        
        // Automation / Sync Services
        services.AddScoped<ITeamStatsSyncService, TeamStatsSyncService>();
        services.AddScoped<ITeamMappingService, TeamMappingService>();
        services.AddScoped<IFixtureGenerationService, FixtureGenerationService>();
        
        // Background Workers
        services.AddHostedService<NightlySyncWorker>();
        services.AddHostedService<EuropeanFixturesUpdateService>();
        
        // ML Services
        services.AddScoped<soccer_gpt_infrastructure.Services.ML.FeatureEngineeringService>();
        services.AddSingleton<soccer_gpt_infrastructure.Services.ML.SoccerGoalScoringModel>();
        services.AddScoped<IMlPredictionService, soccer_gpt_infrastructure.Services.ML.MlPredictionService>();
        services.AddScoped<soccer_gpt_infrastructure.Services.Analysis.MlBacktestService>();
        services.AddScoped<soccer_gpt_infrastructure.Services.Analysis.GeminiBacktestService>();
        
        // Background Services
        services.AddHostedService<DataPreloaderService>();
        
        // Gemini Pipeline
        services.AddHttpClient<IGeminiService, GeminiService>(); // Use Typed Client
        services.AddScoped<EnhancedAnalysisService>();
        
        // Gemini Batch Analysis Service (inline batch calls)
        services.AddScoped<IGeminiAnalysisService, GeminiAnalysisService>();
        
        // Advanced Feature Services
        services.AddScoped<soccer_gpt_infrastructure.Services.Analysis.RefereeAnalysisService>();
        services.AddScoped<soccer_gpt_infrastructure.Services.Analysis.CongestionAnalysisService>();

        return services;
    }
}
