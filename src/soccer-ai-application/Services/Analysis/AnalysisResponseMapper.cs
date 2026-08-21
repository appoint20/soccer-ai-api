using SoccerAi.Application.Helpers;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Features.Analysis;
using SoccerAi.Application.Entities;

namespace SoccerAi.Application.Services.Analysis;

/// <summary>
/// Maps FixtureAnalysisResult domain models to MatchAnalysis response DTOs.
/// Extracted from GetMatchAnalysisHandler to promote single responsibility.
///
/// Responsibilities:
/// 1. Convert domain analysis to response format
/// 2. Enrich team stats with standing information
/// 3. Validate prediction accuracy for completed matches
/// 4. Integrate AI analysis
/// 5. Calculate summary statistics
/// </summary>
public class AnalysisResponseMapper
{
    /// <summary>
    /// Maps analyzed fixture to response DTO.
    /// </summary>
    public static MatchAnalysis MapToResponse(
        Fixture fixture,
        FixtureAnalysisResult analysis,
        Team homeTeam,
        Team awayTeam,
        AiAnalysisDto? aiAnalysis)
    {
        var prediction = BuildPredictionResponse(analysis, aiAnalysis);
        var matchResult = ValidateMatchResult(fixture, analysis);
        var headline = BuildHeadline(analysis.Prediction, matchResult);

        // Production Sanitization: Only show models in Development
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        var includeModels = env.Equals("Development", StringComparison.OrdinalIgnoreCase);

        return new MatchAnalysis
        {
            Id = fixture.Id,
            Date = fixture.Date,
            Time = fixture.Date.TimeOfDay,
            League = analysis.LeagueName,
            HomeTeam = homeTeam.ShortName ?? homeTeam.Name,
            AwayTeam = awayTeam.ShortName ?? awayTeam.Name,
            Result = matchResult,
            Headline = headline,
            OddsHomeWin = analysis.OddsHomeWin,
            OddsDraw = analysis.OddsDraw,
            OddsAwayWin = analysis.OddsAwayWin,
            OddsOver25 = analysis.OddsOver25,
            OddsUnder25 = analysis.OddsUnder25,
            OddsBttsYes = analysis.OddsBttsYes,
            // Same-match doubles are priced off the joint, and Models is not
            // serialized into the snapshot — so carry the joint explicitly.
            BttsAndOver25Probability = analysis.Models.Poisson is { IsValid: true } poisson
                ? poisson.BttsAndOver25
                : null,
            HomeStats = analysis.TeamStats.Home,
            AwayStats = analysis.TeamStats.Away,
            Models = includeModels ? analysis.Models : null,
            Prediction = prediction,
            Trap = aiAnalysis?.IsTrap == true 
                ? new TrapDecision { IsTrap = true, Reason = aiAnalysis.TrapReason } 
                : analysis.Decisions.Trap,
            H2H = analysis.H2H,
            Ai = (aiAnalysis == null || (string.IsNullOrWhiteSpace(aiAnalysis.Recommendation) && aiAnalysis.Confidence == 0))
                ? new AiAnalysisDto()
                : aiAnalysis,
            Signals = analysis.Signals,
            DecisionAudit = analysis.Decisions.Audit,
            CalibrationTrace = analysis.CalibrationTrace
        };
    }

    /// <summary>
    /// Builds prediction response with persisted AI reasoning.
    /// </summary>
    private static PredictionResponse? BuildPredictionResponse(
        FixtureAnalysisResult analysis,
        AiAnalysisDto? ai)
    {
        if (analysis.Prediction == null)
            return null;

        var wp = analysis.Prediction;
        var d = analysis.Decisions;

        return new PredictionResponse
        {
            Over25 = new BoolPrediction
            {
                Prediction = wp.Over25,
                Probability = Math.Round(wp.Over25Prob, 2),
                IsQualified = d.Markets.Over25.IsQualified,
                Reason = !string.IsNullOrWhiteSpace(ai?.Over25Summary)
                    ? ai.Over25Summary
                    : d.Markets.Over25.Reason
            },
            BTTS = new BoolPrediction
            {
                Prediction = wp.BTTS,
                Probability = Math.Round(wp.BTTSProb, 2),
                IsQualified = d.Markets.BTTS.IsQualified,
                Reason = !string.IsNullOrWhiteSpace(ai?.BttsSummary)
                    ? ai.BttsSummary
                    : d.Markets.BTTS.Reason
            },
            TwoToThreeGoals = new BoolPrediction
            {
                Prediction = wp.TwoToThreeGoals,
                Probability = Math.Round(wp.TwoToThreeGoalsProb, 2),
                IsQualified = d.Markets.TwoToThreeGoals.IsQualified,
                Reason = d.Markets.TwoToThreeGoals.Reason
            },
            LowScoring = new BoolPrediction
            {
                Prediction = d.Markets.LowScoring.IsQualified,
                Probability = d.Markets.LowScoring.Confidence,
                IsQualified = d.Markets.LowScoring.IsQualified,
                Reason = !string.IsNullOrWhiteSpace(ai?.Under25Summary)
                    ? ai.Under25Summary
                    : d.Markets.LowScoring.Reason
            },
            HomeWin = new BoolPrediction
            {
                Prediction = wp.MatchWinner.Equals("home", StringComparison.OrdinalIgnoreCase),
                Probability = wp.HomeProb,
                IsQualified = d.Markets.MatchWinner.IsQualified && wp.MatchWinner.Equals("home", StringComparison.OrdinalIgnoreCase),
                Reason = GetWinnerReason(ai, "home", d.Markets.MatchWinner.Reason)
            },
            Draw = new BoolPrediction
            {
                Prediction = wp.MatchWinner.Equals("draw", StringComparison.OrdinalIgnoreCase),
                Probability = wp.DrawProb,
                IsQualified = d.Markets.MatchWinner.IsQualified && wp.MatchWinner.Equals("draw", StringComparison.OrdinalIgnoreCase),
                Reason = d.Markets.MatchWinner.Reason
            },
            AwayWin = new BoolPrediction
            {
                Prediction = wp.MatchWinner.Equals("away", StringComparison.OrdinalIgnoreCase),
                Probability = wp.AwayProb,
                IsQualified = d.Markets.MatchWinner.IsQualified && wp.MatchWinner.Equals("away", StringComparison.OrdinalIgnoreCase),
                Reason = GetWinnerReason(ai, "away", d.Markets.MatchWinner.Reason)
            },
            MatchWinner = new StringPrediction
            {
                Prediction = wp.MatchWinner,
                Confidence = wp.Confidence,
                IsQualified = d.Markets.MatchWinner.IsQualified,
                Reason = GetWinnerReason(ai, wp.MatchWinner, d.Markets.MatchWinner.Reason)
            }
        };
    }

    /// <summary>
    /// Picks the single call the system stands behind: the highest-probability
    /// market on the fixture.
    ///
    /// Same rule as the confidence picks, so the headline shown on a match and
    /// the pick the product sells cannot contradict each other. Grading one call
    /// rather than every market also stops a match reading as "3 of 4 correct"
    /// when the thing the system actually backed was wrong.
    /// </summary>
    private static HeadlinePrediction? BuildHeadline(WeightedPrediction? p, MatchResult? result)
    {
        if (p is null) return null;

        // Each market as the side the model actually leans to, with that side's
        // probability — a 30% "over" is a 70% "under", and the call is the under.
        var candidates = new (string Market, string Selection, double Probability, bool? Correct)[]
        {
            p.Over25
                ? ("over_2_5", "Over 2.5 Goals", p.Over25Prob, result is null ? null : result.ActualOver25 == true)
                : ("under_2_5", "Under 2.5 Goals", 1 - p.Over25Prob, result is null ? null : result.ActualOver25 == false),

            p.BTTS
                ? ("btts", "Both Teams To Score", p.BTTSProb, result is null ? null : result.ActualBtts == true)
                : ("no_btts", "Not Both Teams To Score", 1 - p.BTTSProb, result is null ? null : result.ActualBtts == false),

            p.MatchWinner.Equals("home", StringComparison.OrdinalIgnoreCase)
                ? ("home_win", "Home Win", p.HomeProb, result is null ? null : result.ActualWinner == "home")
                : p.MatchWinner.Equals("away", StringComparison.OrdinalIgnoreCase)
                    ? ("away_win", "Away Win", p.AwayProb, result is null ? null : result.ActualWinner == "away")
                    : ("draw", "Draw", p.DrawProb, result is null ? null : result.ActualWinner == "draw"),
        };

        // A market whose probability is exactly 0 was not computed — treat it as
        // absent rather than as a certainty. Without this the complement of an
        // unset probability is 1.0, and a market the model never priced wins the
        // headline slot as a 100% confident call.
        var best = candidates
            .Where(c => c.Probability is > 0 and < 1)
            .OrderByDescending(c => c.Probability)
            .FirstOrDefault();

        if (best.Market is null) return null;

        return new HeadlinePrediction
        {
            Market = best.Market,
            Selection = best.Selection,
            Probability = Math.Round(best.Probability, 4),
            IsCorrect = best.Correct,
        };
    }

    /// <summary>
    /// Validates match result for completed fixtures.
    /// Supports variety of completed statuses from API-Football.
    /// </summary>
    /// <summary>
    /// Maps an API-Football status to a countable outcome.
    /// </summary>
    /// <remarks>
    /// Only a match played to its conclusion settles. An awarded result or a
    /// walkover has a scoreline but no market outcome — nobody's Over 2.5 was
    /// decided by a forfeit — so those are void rather than settled, and an
    /// abandoned match is reported as such instead of being scored on the goals
    /// that happened to be in before it stopped.
    /// </remarks>
    private static string? ClassifyResultStatus(string? fixtureStatus) => fixtureStatus switch
    {
        "FT" or "AET" or "PEN" => ResultStatus.Settled,
        "ABD" => ResultStatus.Abandoned,
        "AWD" or "WO" or "CANC" => ResultStatus.Void,
        "PST" or "SUSP" or "INT" => ResultStatus.Postponed,
        // Not yet played, or still in progress: there is no result to report.
        _ => null,
    };

    /// <summary>
    /// One verdict per market the fixture can be scored on, keyed as in
    /// <c>decision_audit.markets[]</c>.
    /// </summary>
    /// <remarks>
    /// A call is correct when it matches the outcome, including when it
    /// correctly said a market would NOT hit. Markets are omitted rather than
    /// reported false when no prediction exists, because "not judged" and
    /// "wrong" are different facts on screen.
    /// </remarks>
    private static List<MarketVerdict> BuildMarketVerdicts(
        WeightedPrediction? p, bool isBtts, bool isOver25, int totalGoals, string actualWinner)
    {
        if (p is null) return [];

        var verdicts = new List<MarketVerdict>
        {
            new("btts", p.BTTS == isBtts),
            new("over25", p.Over25 == isOver25),

            // Over and Under 2.5 are one binary call: getting "over" wrong is
            // the same event as getting "under" wrong, so the verdict is shared.
            new("under25", p.Over25 == isOver25),

            new("goals_2_3", p.TwoToThreeGoals == totalGoals is 2 or 3),
        };

        if (!string.IsNullOrWhiteSpace(p.MatchWinner))
        {
            verdicts.Add(new MarketVerdict("match_winner",
                p.MatchWinner.Equals(actualWinner, StringComparison.OrdinalIgnoreCase)));

            // The draw is its own market in the audit, so it gets its own
            // verdict: did the model correctly say this would (or would not)
            // finish level.
            var calledDraw = p.MatchWinner.Equals("draw", StringComparison.OrdinalIgnoreCase);
            verdicts.Add(new MarketVerdict("draw", calledDraw == (actualWinner == "draw")));
        }

        return verdicts;
    }

    private static MatchResult? ValidateMatchResult(Fixture fixture, FixtureAnalysisResult analysis)
    {
        var status = ClassifyResultStatus(fixture.Status);
        if (status is null)
            return null;

        var actualScore = $"{fixture.HomeGoal}:{fixture.AwayGoal}";
        var totalGoals = fixture.HomeGoal + fixture.AwayGoal;
        var isBtts = fixture.HomeGoal > 0 && fixture.AwayGoal > 0;

        string predWinner = analysis.Prediction?.MatchWinner ?? "";
        bool isWinnerCorrect =
            (predWinner.Equals("home", StringComparison.OrdinalIgnoreCase) && fixture.HomeGoal > fixture.AwayGoal) ||
            (predWinner.Equals("draw", StringComparison.OrdinalIgnoreCase) && fixture.HomeGoal == fixture.AwayGoal) ||
            (predWinner.Equals("away", StringComparison.OrdinalIgnoreCase) && fixture.HomeGoal < fixture.AwayGoal);

        var isOver25 = totalGoals > 2.5;
        var actualWinner = fixture.HomeGoal > fixture.AwayGoal ? "home"
            : fixture.HomeGoal < fixture.AwayGoal ? "away" : "draw";

        // A prediction is correct when it matches the outcome — including when
        // it correctly predicted the market would NOT hit. These flags used to
        // carry the raw outcome, so "BTTS: no" on a 1-0 was reported as wrong.
        var p = analysis.Prediction;

        // Verdicts only mean something for a match that actually played out.
        // A void or postponed fixture reports its status and no markets, so a
        // client counting hits skips it instead of recording a loss.
        var isSettled = status == ResultStatus.Settled;

        return new MatchResult
        {
            Status = status,
            Markets = isSettled
                ? BuildMarketVerdicts(p, isBtts, isOver25, totalGoals, actualWinner)
                : [],

            ActualScore = actualScore,
            IsCorrect = isSettled && isWinnerCorrect,

            IsBttsCorrect = p is null || !isSettled ? null : p.BTTS == isBtts,
            IsOver25Correct = p is null || !isSettled ? null : p.Over25 == isOver25,

            // Over and Under 2.5 are one binary call, so this necessarily equals
            // IsOver25Correct: predicting "over" wrongly is the same event as
            // predicting "under" wrongly. Kept as its own field so a UI showing
            // an Under 2.5 row does not have to invert anything itself.
            IsUnder25Correct = p is null || !isSettled ? null : p.Over25 == isOver25,

            HomeGoals = fixture.HomeGoal,
            AwayGoals = fixture.AwayGoal,
            TotalGoals = totalGoals,
            ActualBtts = isBtts,
            ActualOver25 = isOver25,
            PredictedWinner = string.IsNullOrWhiteSpace(predWinner) ? null : predWinner.ToLowerInvariant(),
            ActualWinner = actualWinner,
        };
    }

    /// <summary>
    /// Gets winner prediction reason from AI output or fallback logic.
    /// </summary>
    private static string GetWinnerReason(AiAnalysisDto? ai, string winner, string defaultReason)
    {
        var w = winner.ToLowerInvariant();
        if (w == "home" && !string.IsNullOrWhiteSpace(ai?.HomeWinSummary))
            return ai.HomeWinSummary;
        if (w == "away" && !string.IsNullOrWhiteSpace(ai?.AwayWinSummary))
            return ai.AwayWinSummary;
        return defaultReason;
    }

    /// <summary>
    /// Calculates summary statistics for batch of matches.
    /// </summary>
    public static AnalysisSummary CalculateSummary(List<MatchAnalysis> matches)
    {
        var finished = matches.Where(m => m.Result != null).ToList();

        if (!finished.Any())
            return new AnalysisSummary { TotalMatches = 0, CorrectMatches = 0, AccuracyRate = 0 };

        var correct = finished.Count(m => m.Result!.IsCorrect);
        var accuracy = Math.Round((double)correct / finished.Count * 100, 2);

        return new AnalysisSummary
        {
            TotalMatches = finished.Count,
            CorrectMatches = correct,
            AccuracyRate = accuracy
        };
    }
}
