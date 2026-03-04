using SoccerAi.Application.Interfaces;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Professional League Adjustment Service.
/// Lower leagues naturally score less; requiring equivalent probabilities/EV to elite leagues 
/// needlessly chokes off volume. We manually adjust threshold requirements here.
/// </summary>
public sealed class LeagueAdjustmentService : ILeagueAdjustmentService
{
    public double GetGoalThresholdModifier(string leagueName)
    {
        var lowerName = leagueName.ToLowerInvariant();

        // ── High Scoring / Elite Leagues ──
        // Baseline 0 modification (or perfectly neutral).
        // Bundesliga, Eredivisie, Serie A (recently)
        if (lowerName.Contains("bundesliga") || 
            lowerName.Contains("eredivisie"))
        {
            return 0.0; // Standard threshold
        }

        // ── Average Scoring Leagues ──
        if (lowerName.Contains("premier league") || 
            lowerName.Contains("serie a") ||
            lowerName.Contains("championship"))
        {
            return -2.0; // Effectively lowers the ~60pt bar to ~58
        }

        // ── Lower Scoring / Tactical Leagues ──
        // La Liga, Ligue 1, Liga NOS, lower divisions. 
        // These need help qualifying for Goal Markets because baseline models punish them too harshly.
        if (lowerName.Contains("la liga") || 
            lowerName.Contains("ligue 1") || 
            lowerName.Contains("primeira liga") ||
            lowerName.Contains("serie b") ||
            lowerName.Contains("ligue 2"))
        {
            return -5.0; // Major shift: lowers the ~60pt requirement to ~55
        }

        // Default
        return -2.0;
    }
}
