using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services;

namespace SoccerAi.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ITeamStatsService, TeamStatsService>();
        services.AddScoped<IDixonColesModel, DixonColesModel>();
        services.AddScoped<IMarketCalibrationService, MarketCalibrator>();
        services.AddSingleton<ILeagueTierService, LeagueTierService>();
        services.AddScoped<IStrategicSignalService, Services.Signals.StrategicSignalService>();
        services.AddOptions<Options.DixonColesOptions>();
        services.AddOptions<Options.CalibrationOptions>();
        services.AddOptions<Options.LeagueTierOptions>();
        services.AddOptions<Options.StrategyOptions>();
        services.AddScoped<IChatCombinationEngine, Services.Combinations.ChatCombinationEngine>();
        services.AddScoped<ICombinationService, Services.Combinations.DeterministicCombinationService>();

        // Helpers and Pipeline Services
        services.AddScoped<Helpers.FixtureQueryHelper>();
        services.AddScoped<Services.Analysis.AnalysisResponseMapper>();

        // FluentValidation — auto-register all validators in this assembly
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}

