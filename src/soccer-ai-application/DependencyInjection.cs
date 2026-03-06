using Microsoft.Extensions.DependencyInjection;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services;

namespace SoccerAi.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ITeamStatsService, TeamStatsService>();
        services.AddScoped<IMonteCarloService, MonteCarloService>();
        services.AddScoped<IPoissonCalculationService, PoissonCalculationService>();

        // Helpers and Pipeline Services
        services.AddScoped<Helpers.FixtureQueryHelper>();
        services.AddScoped<Services.Analysis.AnalysisResponseMapper>();


        return services;
    }
}
