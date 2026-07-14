using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Comprehensive trap detection: low-score traps, market disagreements,
/// ultra-defensive setups, and relegation motivation traps.
/// Uses LowScoreDetector for Poisson math.
/// </summary>
public sealed class TrapDetectionService : ITrapDetectionService
{
    private const double MarketDisagreementThreshold = 0.15;

    /// <summary>
    /// Relegation zone sizes by league. Bottom N teams get relegated.
    /// </summary>
    private static int GetRelegationZoneSize(string leagueName) => leagueName switch
    {
        "Premier League" => 3,
        "Bundesliga" => 3,     // 2 direct + 1 playoff, treat all 3 as danger zone
        "Serie A" => 3,
        "La Liga" => 3,
        "Ligue 1" => 3,        // 2 direct + 1 playoff
        "Championship" => 4,    // Bottom 4 (positions 21-24)
        "League One" => 4,
        "League Two" => 2,      // Bottom 2 get relegated to National League
        "2. Bundesliga" => 3,
        "3. Liga" => 4,
        "Serie B" => 3,
        "La Liga 2" => 4,
        "Ligue 2" => 4,
        _ => 3
    };

    /// <summary>
    /// Total teams in the league (for calculating relegation positions).
    /// </summary>
    private static int GetLeagueSize(string leagueName) => leagueName switch
    {
        "Premier League" => 20,
        "Bundesliga" => 18,
        "Serie A" => 20,
        "La Liga" => 20,
        "Ligue 1" => 18,
        "Championship" => 24,
        "League One" => 24,
        "League Two" => 24,
        "2. Bundesliga" => 18,
        "3. Liga" => 20,
        "Serie B" => 20,
        "La Liga 2" => 22,
        "Ligue 2" => 20,
        _ => 20
    };

    public TrapResult Detect(
        ProbabilityBundle bundle,
        WeightedPrediction? prediction,
        MatchContext odds,
        TeamStatsResponse? teamStats = null)
    {
        if (prediction == null) return TrapResult.Safe;

        var lambdaHome = bundle.Poisson.ExpectedHomeGoals;
        var lambdaAway = bundle.Poisson.ExpectedAwayGoals;

        // ── 1. Low score trap (P(0-0) > 18%) ──
        bool lowScoreTrap = bundle.Poisson.IsValid &&
                            LowScoreDetector.IsLowScoringTrap(lambdaHome, lambdaAway);

        // ── 2. Market disagreement (raw model vs market implied differ > 15 pp) ──
        bool marketMismatch = false;
        if (odds.OddsOver25 > 1 && bundle.Poisson.IsValid)
        {
            var marketImpliedOver25 = 1.0 / odds.OddsOver25.Value;
            marketMismatch = Math.Abs(bundle.Poisson.Over25 - marketImpliedOver25)
                             > MarketDisagreementThreshold;
        }

        // ── 3. Ultra-defensive match (both λ < 1.0) ──
        bool defensiveMatch = bundle.Poisson.IsValid &&
                              lambdaHome < 1.0 && lambdaAway < 1.0;

        // ── 4. Relegation motivation trap ──
        bool relegationTrap = false;
        string? relegationReason = null;
        if (teamStats != null && !string.IsNullOrWhiteSpace(odds.LeagueName))
        {
            var (isTrap, reason) = DetectRelegationTrap(
                teamStats.Home, teamStats.Away, odds.LeagueName);
            relegationTrap = isTrap;
            relegationReason = reason;
        }

        // Build reason string and accumulate penalty
        var reasons = new List<string>();
        double penaltyScore = 0;
        
        if (lowScoreTrap)
        {
            var p00 = LowScoreDetector.Probability00(lambdaHome, lambdaAway);
            reasons.Add($"P(0-0) = {p00:P0} (-15 pts)");
            penaltyScore -= 15.0;
        }
        if (marketMismatch) 
        {
            reasons.Add("Model vs market disagreement (-10 pts)");
            penaltyScore -= 10.0;
        }
        if (defensiveMatch) 
        {
            reasons.Add($"Ultra-defensive (λH={lambdaHome:F2}, λA={lambdaAway:F2}) (-10 pts)");
            penaltyScore -= 10.0;
        }
        if (relegationTrap)
        {
            reasons.Add(relegationReason!);
            penaltyScore -= 20.0; // Hard trap — severely unpredictable
        }

        return new TrapResult
        {
            LowScoreTrap = lowScoreTrap,
            MarketMismatch = marketMismatch,
            DefensiveMatch = defensiveMatch,
            RelegationTrap = relegationTrap,
            PenaltyScore = penaltyScore,
            Reason = string.Join("; ", reasons)
        };
    }

    /// <summary>
    /// Detects if either team is relegated or in the relegation zone late in the season.
    /// Teams in this position often show no motivation — results become unpredictable.
    /// A team with good recent form (e.g., 3 wins) but doomed overall is especially dangerous
    /// because the form misleads the model into over-confidence.
    /// </summary>
    private static (bool IsTrap, string? Reason) DetectRelegationTrap(
        TeamStats home, TeamStats away, string leagueName)
    {
        int leagueSize = GetLeagueSize(leagueName);
        int relegationZone = GetRelegationZoneSize(leagueName);
        // Late season = 75%+ of matches played (e.g., 30 of 38, or 28 of 34)
        int lateSeasonThreshold = (int)Math.Ceiling((leagueSize - 1) * 2 * 0.75);

        var reasons = new List<string>();

        CheckTeamRelegation(home, "Home", leagueSize, relegationZone, lateSeasonThreshold, reasons);
        CheckTeamRelegation(away, "Away", leagueSize, relegationZone, lateSeasonThreshold, reasons);

        if (reasons.Count > 0)
            return (true, string.Join("; ", reasons));
        
        return (false, null);
    }

    private static void CheckTeamRelegation(
        TeamStats team, string label, int leagueSize, int relegationZone, int lateSeasonThreshold,
        List<string> reasons)
    {
        // Must be late in the season
        if (team.Played < lateSeasonThreshold)
            return;

        int relegationStart = leagueSize - relegationZone + 1;
        bool isInRelegationZone = team.Rank >= relegationStart;

        if (!isInRelegationZone)
            return;

        // Team is in relegation zone late in the season
        // Check if they have deceptively good recent form (masking the doom)
        bool hasGoodRecentForm = team.FormPercentage >= 60; // 60%+ form = winning streak
        
        if (hasGoodRecentForm)
        {
            reasons.Add(
                $"⚠️ {label} team '{team.Name}' is in relegation zone (Rank {team.Rank}/{leagueSize}, {team.Points} pts) " +
                $"despite good recent form ({team.Form}). Likely relegated — no motivation (Abstieg). (-20 pts)");
        }
        else
        {
            reasons.Add(
                $"⚠️ {label} team '{team.Name}' is in relegation zone (Rank {team.Rank}/{leagueSize}, {team.Points} pts, Form: {team.Form}). " +
                $"Likely relegated — no motivation, results unpredictable (Abstieg). (-20 pts)");
        }
    }
}

