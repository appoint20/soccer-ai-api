using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Features.Combinations;

namespace SoccerAi.Application.Services.Combinations;

/// <summary>
/// Builds combination portfolios from analyzed fixtures.
/// Extracted from GetMatchCombinationHandler to promote single responsibility.
///
/// Workflow:
/// 1. Takes analyzed fixtures with decisions and market qualifications
/// 2. Filters qualified markets based on league specialization rules
/// 3. Creates raw candidate list with odds normalization
/// 4. Segments candidates into goal (O2.5/BTTS) and winner portfolios
/// 5. Builds parlays from uncorrelated markets (distinct fixtures)
/// 6. Returns typed combination DTOs
/// </summary>
public class CombinationPortfolioBuilder(
    IExpectedValueEngine evEngine,
    ILogger<CombinationPortfolioBuilder> logger)
{
    // Odds configuration for combination building
    private const double MinOdds = 2.00;
    private const double MinGoalOdds = 1.65;
    private const double MaxOdds = 5.00;

    /// <summary>
    /// Builds combination portfolio from fixture analysis results.
    /// </summary>
    public async Task<List<CombinationDto>> BuildPortfolioAsync(
        List<Fixture> fixtures,
        Dictionary<int, Team> teams,
        Dictionary<int, FixtureAnalysisResult> analysisMap,
        CancellationToken cancellationToken = default)
    {
        var teamNames = teams.ToDictionary(x => x.Key, x => x.Value.Name);
        var rawCandidates = new List<CombinationMatchDto>();

        // Step 1: Gather raw candidates from analyzed fixtures
        foreach (var fixture in fixtures)
        {
            try
            {
                if (!analysisMap.TryGetValue(fixture.Id, out var analysis))
                    continue;

                if (analysis.Prediction == null || analysis.Decisions.Decision == PredictionDecision.Avoid)
                    continue;

                if (analysis.Decisions.Trap.IsTrap)
                    continue;

                var candidates = BuildFixtureCandidates(fixture, analysis, teamNames);
                rawCandidates.AddRange(candidates);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error processing fixture {Id}", fixture.Id);
            }
        }

        logger.LogInformation("Raw candidates gathered: {Count}", rawCandidates.Count);

        // Step 2: Filter by decision quality
        var targetDecisions = new[]
        {
            PredictionDecision.StrongBet.ToString(),
            PredictionDecision.SmallEdge.ToString(),
            PredictionDecision.LeanBet.ToString()
        };

        var goalPortfolio = FilterGoalBets(rawCandidates, targetDecisions);
        var winnerPortfolio = FilterWinnerBets(rawCandidates);

        logger.LogInformation("Portfolios: {GoalCount} goal, {WinnerCount} winner",
            goalPortfolio.Count, winnerPortfolio.Count);

        // Step 3: Build combinations from unique fixtures
        return BuildCombinations(goalPortfolio, winnerPortfolio);
    }

    /// <summary>
    /// Extracts all qualified market candidates for a fixture.
    /// </summary>
    private List<CombinationMatchDto> BuildFixtureCandidates(
        Fixture fixture,
        FixtureAnalysisResult analysis,
        Dictionary<int, string> teamNames)
    {
        var candidates = new List<CombinationMatchDto>();
        var wp = analysis.Prediction;
        var decisions = analysis.Decisions;
        var homeName = teamNames.GetValueOrDefault(fixture.HomeTeamId, $"Team {fixture.HomeTeamId}");
        var awayName = teamNames.GetValueOrDefault(fixture.AwayTeamId, $"Team {fixture.AwayTeamId}");
        var leagueName = analysis.LeagueName;
        var decision = decisions.Decision.ToString();

        bool isDualQualified = decisions.Markets.Over25?.IsQualified == true &&
                               decisions.Markets.BTTS?.IsQualified == true;
        double requiredMinGoalOdds = isDualQualified ? 1.50 : 1.65;

        // Over 2.5 Goals market
        if (decisions.Markets.Over25?.IsQualified == true &&
            !IsLeagueMarketExcluded(leagueName, "Over25"))
        {
            var o25Candidate = BuildOver25Candidate(
                fixture, analysis, teamNames, isDualQualified, requiredMinGoalOdds);

            if (o25Candidate != null)
                candidates.Add(o25Candidate);
        }

        // BTTS market
        if (decisions.Markets.BTTS?.IsQualified == true &&
            !IsLeagueMarketExcluded(leagueName, "BTTS"))
        {
            var bttsCadidate = BuildBttsCandidate(
                fixture, analysis, teamNames, isDualQualified, requiredMinGoalOdds);

            if (bttsCadidate != null)
                candidates.Add(bttsCadidate);
        }

        // Match Winner market
        if (decisions.Markets.MatchWinner?.IsQualified == true &&
            !IsLeagueMarketExcluded(leagueName, "MatchWinner"))
        {
            var winnerCandidate = BuildMatchWinnerCandidate(fixture, analysis, teamNames);

            if (winnerCandidate != null)
                candidates.Add(winnerCandidate);
        }

        return candidates;
    }

    /// <summary>
    /// Builds Over 2.5 Goals market candidate if odds are qualified.
    /// </summary>
    private CombinationMatchDto? BuildOver25Candidate(
        Fixture fixture,
        FixtureAnalysisResult analysis,
        Dictionary<int, string> teamNames,
        bool isDualQualified,
        double requiredMinGoalOdds)
    {
        var wp = analysis.Prediction!;
        var decisions = analysis.Decisions;
        double odds = NormalizeOdds(fixture.Over25Odds);
        double ev = odds > 1 ? evEngine.CalculateEV(wp.Over25Prob, odds) : 0;
        double effectiveOdds = odds > 1 ? odds : 1.80;

        if (effectiveOdds < requiredMinGoalOdds || effectiveOdds > MaxOdds)
            return null;

        double adjustedConfidence = isDualQualified ? wp.Over25Prob * 1.05 : wp.Over25Prob;

        return new CombinationMatchDto(
            fixture.Id,
            fixture.LeagueId,
            analysis.LeagueName,
            fixture.Date,
            teamNames.GetValueOrDefault(fixture.HomeTeamId) ?? string.Empty,
            teamNames.GetValueOrDefault(fixture.AwayTeamId) ?? string.Empty,
            "Over 2.5 Goals",
            "Over",
            Math.Round(adjustedConfidence, 2),
            effectiveOdds,
            fixture.Status,
            fixture.Status == "FT" ? fixture.HomeGoal : null,
            fixture.Status == "FT" ? fixture.AwayGoal : null,
            false,
            true,
            false,
            decisions.Markets.Over25?.Reason + (isDualQualified ? " (Dual-Qualified)" : "") ?? "",
            Math.Round(ev, 4),
            decisions.Decision.ToString(),
            analysis.Gemini?.Recommendation ?? "",
            analysis.Gemini?.Confidence ?? 0,
            analysis.Gemini?.IsTrap ?? false,
            analysis.Gemini?.TrapReason ?? "",
            analysis.Gemini?.OneLineSummary ?? "",
            analysis.Gemini?.Reasoning ?? "",
            analysis.Gemini?.Analysis ?? ""
        );
    }

    /// <summary>
    /// Builds BTTS market candidate if odds and technical checks pass.
    /// </summary>
    private CombinationMatchDto? BuildBttsCandidate(
        Fixture fixture,
        FixtureAnalysisResult analysis,
        Dictionary<int, string> teamNames,
        bool isDualQualified,
        double requiredMinGoalOdds)
    {
        var wp = analysis.Prediction!;
        var decisions = analysis.Decisions;
        var models = analysis.Models;

        // Safety checks: avoid blowout scenarios
        bool isBlowoutRisk = fixture.HomeWinOdds < 1.85 || fixture.AwayWinOdds < 1.85;
        bool hasScoringCapability = models.Poisson.IsValid &&
                                    models.Poisson.ExpectedHomeGoals >= 1.05 &&
                                    models.Poisson.ExpectedAwayGoals >= 1.05;

        if (isBlowoutRisk || !hasScoringCapability)
            return null;

        double odds = NormalizeOdds(fixture.BttsYesOdds);
        double ev = odds > 1 ? evEngine.CalculateEV(wp.BTTSProb, odds) : 0;
        double effectiveOdds = odds > 1 ? odds : 1.80;

        if (effectiveOdds < requiredMinGoalOdds || effectiveOdds > MaxOdds)
            return null;

        double adjustedConfidence = isDualQualified ? wp.BTTSProb * 1.05 : wp.BTTSProb;

        return new CombinationMatchDto(
            fixture.Id,
            fixture.LeagueId,
            analysis.LeagueName,
            fixture.Date,
            teamNames.GetValueOrDefault(fixture.HomeTeamId) ?? string.Empty,
            teamNames.GetValueOrDefault(fixture.AwayTeamId) ?? string.Empty,
            "Both Teams To Score",
            "Yes",
            Math.Round(adjustedConfidence, 2),
            effectiveOdds,
            fixture.Status,
            fixture.Status == "FT" ? fixture.HomeGoal : null,
            fixture.Status == "FT" ? fixture.AwayGoal : null,
            false,
            false,
            true,
            decisions.Markets.BTTS?.Reason + (isDualQualified ? " (Dual-Qualified)" : "") ?? "",
            Math.Round(ev, 4),
            decisions.Decision.ToString(),
            analysis.Gemini?.Recommendation ?? "",
            analysis.Gemini?.Confidence ?? 0,
            analysis.Gemini?.IsTrap ?? false,
            analysis.Gemini?.TrapReason ?? "",
            analysis.Gemini?.OneLineSummary ?? "",
            analysis.Gemini?.Reasoning ?? "",
            analysis.Gemini?.Analysis ?? ""
        );
    }

    /// <summary>
    /// Builds Match Winner market candidate.
    /// </summary>
    private CombinationMatchDto? BuildMatchWinnerCandidate(
        Fixture fixture,
        FixtureAnalysisResult analysis,
        Dictionary<int, string> teamNames)
    {
        var wp = analysis.Prediction!;
        var decisions = analysis.Decisions;

        string pred = wp.MatchWinner;
        double? rawOdds = pred.Equals("home", StringComparison.OrdinalIgnoreCase) ? fixture.HomeWinOdds :
                         pred.Equals("away", StringComparison.OrdinalIgnoreCase) ? fixture.AwayWinOdds :
                         fixture.DrawOdds;
        double odds = NormalizeOdds(rawOdds);
        double effectiveOdds = odds > 1.1 ? odds : 2.05;
        double ev = evEngine.CalculateEV(wp.Confidence, effectiveOdds);

        if (effectiveOdds < 1.30 || effectiveOdds > MaxOdds)
            return null;

        string displayPred = char.ToUpper(pred[0]) + pred[1..];
        logger.LogDebug("Match {Id} Market {Market} qualified",
            fixture.Id, "Match Winner");

        return new CombinationMatchDto(
            fixture.Id,
            fixture.LeagueId,
            analysis.LeagueName,
            fixture.Date,
            teamNames.GetValueOrDefault(fixture.HomeTeamId) ?? string.Empty,
            teamNames.GetValueOrDefault(fixture.AwayTeamId) ?? string.Empty,
            "Match Winner",
            displayPred,
            wp.Confidence,
            effectiveOdds,
            fixture.Status,
            fixture.Status == "FT" ? fixture.HomeGoal : null,
            fixture.Status == "FT" ? fixture.AwayGoal : null,
            false,
            true,
            false,
            decisions.Markets.MatchWinner?.Reason ?? "",
            Math.Round(ev, 4),
            decisions.Decision.ToString(),
            analysis.Gemini?.Recommendation ?? "",
            analysis.Gemini?.Confidence ?? 0,
            analysis.Gemini?.IsTrap ?? false,
            analysis.Gemini?.TrapReason ?? "",
            analysis.Gemini?.OneLineSummary ?? "",
            analysis.Gemini?.Reasoning ?? "",
            analysis.Gemini?.Analysis ?? ""
        );
    }

    /// <summary>
    /// Filters goal-based bets by decision quality and confidence.
    /// </summary>
    private List<CombinationMatchDto> FilterGoalBets(
        List<CombinationMatchDto> candidates,
        string[] targetDecisions)
    {
        return candidates
            .Where(x => (x.Market == "Over 2.5 Goals" || x.Market == "Both Teams To Score") &&
                       targetDecisions.Contains(x.Decision))
            .OrderByDescending(x => x.Confidence)
            .ThenByDescending(x => x.ExpectedValue)
            .ToList();
    }

    /// <summary>
    /// Filters winner bets by decision quality.
    /// </summary>
    private List<CombinationMatchDto> FilterWinnerBets(
        List<CombinationMatchDto> candidates)
    {
        return candidates
            .Where(x => x.Market == "Match Winner" &&
                       (x.Decision == "StrongBet" || x.Decision == "SmallEdge"))
            .OrderByDescending(x => x.Confidence)
            .ThenByDescending(x => x.ExpectedValue)
            .ToList();
    }

    /// <summary>
    /// Builds parlays from uncorrelated markets (distinct fixtures).
    /// </summary>
    private List<CombinationDto> BuildCombinations(
        List<CombinationMatchDto> goalPortfolio,
        List<CombinationMatchDto> winnerPortfolio)
    {
        var combinations = new List<CombinationDto>();

        // Ensure no overlapping fixtures within portfolios
        var uniqueGoals = goalPortfolio.GroupBy(x => x.FixtureId).Select(g => g.First()).ToList();
        var uniqueWinners = winnerPortfolio.GroupBy(x => x.FixtureId).Select(g => g.First()).ToList();

        // Build doubles and triples from goal bets
        if (uniqueGoals.Count >= 2)
            combinations.Add(new CombinationDto("High Value Goals Double", uniqueGoals.Take(2).ToList()));

        if (uniqueGoals.Count >= 5)
            combinations.Add(new CombinationDto("Mixed Goals Treble", uniqueGoals.Skip(2).Take(3).ToList()));

        // Build doubles and triples from winner bets
        if (uniqueWinners.Count >= 2)
            combinations.Add(new CombinationDto("Statistical Winners Double", uniqueWinners.Take(2).ToList()));

        if (uniqueWinners.Count >= 5)
            combinations.Add(new CombinationDto("Elite Match Winner Treble", uniqueWinners.Skip(2).Take(3).ToList()));

        return combinations;
    }

    /// <summary>
    /// Checks if a league-market pair historically underperforms.
    /// </summary>
    private static bool IsLeagueMarketExcluded(string leagueName, string market)
    {
        return market switch
        {
            "Over25" => leagueName is "Serie B" or "Ligue 1" or "League One",
            "BTTS" => leagueName is "Serie A" or "Ligue 1" or "League Two" or "Serie B",
            "MatchWinner" => leagueName == "Ligue 2",
            _ => false
        };
    }

    /// <summary>
    /// Normalizes odds handling both decimal and percentage formats.
    /// </summary>
    private static double NormalizeOdds(double? odds)
    {
        if (!odds.HasValue || odds.Value <= 0)
            return 0;

        return odds.Value > 50 ? odds.Value / 100.0 : odds.Value;
    }
}
