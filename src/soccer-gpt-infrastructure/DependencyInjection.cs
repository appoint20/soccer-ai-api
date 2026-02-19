using Microsoft.Extensions.DependencyInjection;
using soccer_gpt_application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using soccer_gpt_application.Services;
using soccer_gpt_infrastructure.Options;
using soccer_gpt_infrastructure.Persistence;
using soccer_gpt_infrastructure.Services;

namespace soccer_gpt_infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Database context - disable FK to allow fixtures with teams not in Teams table
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            const string dbPath = "/Users/shivm/Workspace/soccer-gpt-api/soccer.db";
            options.UseSqlite($"Data Source={dbPath};Foreign Keys=False");
        });
        
        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

        InitApiFootballService(services, configuration);

        // Services
        services.AddSingleton<IHistoricalDataService, HistoricalDataService>();

        // Sync services
        services.AddScoped<TeamSyncService>();
        services.AddScoped<FixtureSyncService>();
        services.AddHostedService<DataSyncBackgroundService>();
        
        // ML Prediction service
        services.AddSingleton<IMlPredictionService, MlPredictionService>();
        services.AddScoped<IFeatureExtractionService, FeatureExtractionService>();
        
        // Decision + detection services
        services.AddScoped<IDecisionService, DecisionService>();
        services.AddScoped<IMarketCalibrationService, MarketCalibrationServiceImpl>();
        services.AddScoped<IExpectedValueEngine, ExpectedValueEngine>();
        services.AddScoped<ITrapDetectionService, TrapDetectionService>();

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
        
        services.AddHttpClient<IApiFootballService, ApiFootballService>(client =>
        {
            client.BaseAddress = new Uri(apiConfig.BaseUrl);
            client.DefaultRequestHeaders.Add("x-apisports-key", apiConfig.ApiKey);
        });
    }
}

