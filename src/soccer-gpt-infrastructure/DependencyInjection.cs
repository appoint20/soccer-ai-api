using Microsoft.Extensions.DependencyInjection;
using soccer_gpt_application.Interfaces;
using soccer_gpt_infrastructure.Services;
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
        services.AddSingleton<InMemoryHistoricalDataService>();
        services.AddSingleton<IHistoricalDataRepository>(sp => sp.GetRequiredService<InMemoryHistoricalDataService>());
        services.AddHostedService(sp => sp.GetRequiredService<InMemoryHistoricalDataService>());
        services.AddScoped<ITeamStatsService, TeamStatsService>();
        services.AddScoped<IAdvancedStatsService, AdvancedStatsService>();
        services.AddScoped<IPoissonGoalModelService, PoissonGoalModelService>();
        services.AddScoped<ITeamAnalyticsService, TeamAnalyticsService>();
        services.AddScoped<Services.Statistics.DixonColesCalculator>();
        services.AddScoped<Services.Statistics.ValueBettingService>();
        
        
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
        
        // Filter Services
        services.AddScoped<IH2HFilterService, Services.Filters.H2HFilterService>();
        services.AddScoped<IH2HReliabilityService, Services.H2H.H2HReliabilityService>();
        services.AddScoped<IEuropeanFatigueService, EuropeanFatigueService>();
        services.AddScoped<IRecentFormService, RecentFormService>();
        services.AddScoped<IPoissonFailureFilters, Services.Filters.PoissonFailureFilters>();
        
        // Automation / Sync Services
        services.AddScoped<ITeamStatsSyncService, TeamStatsSyncService>();
        services.AddScoped<ITeamMappingService, TeamMappingService>();
        services.AddScoped<IFixtureGenerationService, FixtureGenerationService>();
        
        // Background Workers
        services.AddHostedService<NightlySyncWorker>();
        services.AddHostedService<EuropeanFixturesUpdateService>();
        
        services.AddScoped<IGeminiAnalysisService, GeminiAnalysisService>();
        
        // Advanced Feature Services
        services.AddScoped<Services.Analysis.RefereeAnalysisService>();
        services.AddScoped<Services.Analysis.CongestionAnalysisService>();

        return services;
    }
}
