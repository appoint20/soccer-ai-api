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
        services.AddScoped<IProbabilityCalibrationService, Services.Calibration.ProbabilityCalibrationService>();
        services.AddOptions<Options.DixonColesOptions>();
        services.AddOptions<Options.CalibrationOptions>();
        services.AddOptions<Options.LeagueTierOptions>();
        services.AddOptions<Options.StrategyOptions>();
        services.AddScoped<IChatCombinationEngine, Services.Combinations.ChatCombinationEngine>();
        services.AddScoped<ICombinationService, Services.Combinations.DeterministicCombinationService>();

        // Selection layer: the single source of the picks the product sells.
        // ConfluenceOptions/StrategyOptions are bound from configuration in the
        // infrastructure layer, which owns configuration binding.
        services.AddScoped<IDailyPickService, Services.Decisions.DailyPickService>();
        services.AddScoped<IPickLedger, Services.Decisions.PickLedger>();

        // The forecast head-to-head: model predictions recorded next to the
        // pipeline's own, settled once results land.
        services.AddScoped<Services.Forecasts.IModelForecastLedger, Services.Forecasts.ModelForecastLedger>();

        // Helpers and Pipeline Services
        services.AddScoped<Helpers.FixtureQueryHelper>();
        services.AddScoped<Services.Analysis.AnalysisResponseMapper>();

        // FluentValidation — auto-register all validators in this assembly
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}

