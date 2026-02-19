using Microsoft.Extensions.DependencyInjection;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Services;

namespace soccer_gpt_application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ITeamStatsService, TeamStatsService>();
        services.AddScoped<IMonteCarloService, MonteCarloService>();
        services.AddScoped<IPoissonCalculationService, PoissonCalculationService>();

        return services;
    }
}
