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
            OddsHomeWin = analysis.OddsHomeWin,
            OddsDraw = analysis.OddsDraw,
            OddsAwayWin = analysis.OddsAwayWin,
            OddsOver25 = analysis.OddsOver25,
            OddsBttsYes = analysis.OddsBttsYes,
            HomeStats = analysis.TeamStats.Home,
            AwayStats = analysis.TeamStats.Away,
            Models = includeModels ? analysis.Models : null,
            Prediction = prediction,
            Trap = aiAnalysis?.IsTrap == true 
                ? new TrapDecision { IsTrap = true, Reason = aiAnalysis.TrapReason } 
                : analysis.Decisions.Trap,
            H2H = analysis.H2H,
            Ai = (aiAnalysis == null || (string.IsNullOrWhiteSpace(aiAnalysis.Recommendation) && aiAnalysis.Confidence == 0)) 
                ? null 
                : aiAnalysis
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
    /// Validates match result for completed fixtures.
    /// Supports variety of completed statuses from API-Football.
    /// </summary>
    private static MatchResult? ValidateMatchResult(Fixture fixture, FixtureAnalysisResult analysis)
    {
        var completedStatuses = new[] { "FT", "AET", "PEN", "ABD", "AWD", "WO" };
        if (!completedStatuses.Contains(fixture.Status))
            return null;

        var actualScore = $"{fixture.HomeGoal}:{fixture.AwayGoal}";
        var totalGoals = fixture.HomeGoal + fixture.AwayGoal;
        var isBtts = fixture.HomeGoal > 0 && fixture.AwayGoal > 0;

        if (analysis.Prediction == null)
            return new MatchResult { ActualScore = actualScore, IsCorrect = false };

        string predWinner = analysis.Prediction.MatchWinner;
        bool isWinnerCorrect =
            (predWinner.Equals("home", StringComparison.OrdinalIgnoreCase) && fixture.HomeGoal > fixture.AwayGoal) ||
            (predWinner.Equals("draw", StringComparison.OrdinalIgnoreCase) && fixture.HomeGoal == fixture.AwayGoal) ||
            (predWinner.Equals("away", StringComparison.OrdinalIgnoreCase) && fixture.HomeGoal < fixture.AwayGoal);

        return new MatchResult 
        { 
            ActualScore = actualScore, 
            IsCorrect = isWinnerCorrect,
            IsBttsCorrect = analysis.Prediction.BTTS == isBtts,
            IsOver25Correct = totalGoals > 2.5 == analysis.Prediction.Over25,
            IsUnder25Correct = totalGoals < 2.5 == (analysis.Decisions.Markets.LowScoring.IsQualified)
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
