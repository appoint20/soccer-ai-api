using SoccerAi.Application.Interfaces;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Expected Value betting engine.
/// EV = probability × odds − 1
/// Positive EV means profitable long-term.
/// </summary>
public sealed class ExpectedValueEngine : IExpectedValueEngine
{
    public double CalculateEV(double probability, double? odds)
        => odds.HasValue ? probability * odds.Value - 1.0 : -1.0;

    public bool IsValueBet(double probability, double? odds, double threshold = 0.05)
        => CalculateEV(probability, odds) > threshold;
}
