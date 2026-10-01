using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

/// <summary>
/// One model's goals forecast, with a complete market vector when requested
/// explicitly by the combined-prediction workflow.
/// </summary>
public sealed record GoalsForecast
{
    /// <summary>Model slug that produced this, e.g. "anthropic/claude-sonnet-5".</summary>
    public required string Model { get; init; }

    public required double ExpectedGoals { get; init; }
    public required int PredictedHomeGoals { get; init; }
    public required int PredictedAwayGoals { get; init; }

    /// <summary>Probability of 3+ total goals, 0-1.</summary>
    public required double Over25Probability { get; init; }

    /// <summary>Probability both teams score, 0-1.</summary>
    public required double BttsProbability { get; init; }

    /// <summary>The model's own confidence, 0-1. Recorded for analysis, never used to select a bet.</summary>
    public required double Confidence { get; init; }

    public required string Rationale { get; init; }
    public CombinedMarkets? Markets { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public string? RawResponseJson { get; init; }
    public string? InputHash { get; init; }
}

/// <summary>
/// Produces comparison forecasts alongside the statistical pipeline, or an
/// explicitly requested full-market forecast for the combined workflow.
/// Existing comparison-ledger records are never repurposed as fresh inputs.
/// </summary>
public interface IMatchForecastService
{
    /// <summary>True when a credential and at least one model are configured.</summary>
    bool IsEnabled { get; }

    /// <summary>The configured model slugs, in the order they are queried.</summary>
    IReadOnlyList<string> Models { get; }

    /// <summary>
    /// Forecasts one fixture with every configured model. Models are queried
    /// concurrently and independently: one failing returns fewer forecasts
    /// rather than none, and a total failure returns an empty list. This is a
    /// measurement, so it must never fail the sync that carries it.
    /// </summary>
    Task<IReadOnlyList<GoalsForecast>> ForecastAsync(
        MatchAnalysis analysis, CancellationToken cancellationToken = default);

    Task<GoalsForecast?> ForecastCombinedAsync(MatchAnalysis analysis, string model,
        CancellationToken cancellationToken = default) => Task.FromResult<GoalsForecast?>(null);
}
