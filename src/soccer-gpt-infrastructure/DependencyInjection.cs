using Microsoft.Extensions.DependencyInjection;
using soccer_gpt_application.Interfaces;
using soccer_gpt_infrastructure.Services;
using soccer_gpt_infrastructure.Repositories;

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
        return services;
    }
}
