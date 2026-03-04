namespace SoccerAi.Application.Interfaces;

/// <summary>
/// Calculates Expected Value (EV) for bets.
/// EV = probability × odds − 1
/// Positive EV = profitable long-term bet.
/// </summary>
public interface IExpectedValueEngine
{
    double CalculateEV(double probability, double odds);
    bool IsValueBet(double probability, double odds, double threshold = 0.05);
}
