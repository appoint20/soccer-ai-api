using Microsoft.Extensions.DependencyInjection;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Services;

namespace soccer_gpt_application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Business Logic Services
        services.AddScoped<ITeamStatsService, TeamStatsService>();
        services.AddScoped<ILeagueStatsService, LeagueStatsService>();
        services.AddScoped<IPoissonService, PoissonService>();
        services.AddScoped<IAnalyzeService, AnalyzeService>();
        
        return services;
    }
}
