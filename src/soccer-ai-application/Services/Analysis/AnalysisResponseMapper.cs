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
/// 4. Integrate Gemini AI analysis
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
        GeminiAnalysis? geminiAnalysis)
    {
        var prediction = BuildPredictionResponse(analysis, geminiAnalysis);
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
            Trap = analysis.Decisions.Trap,
            H2H = analysis.H2H,
            Gemini = geminiAnalysis
        };
    }

    /// <summary>
    /// Builds prediction response with Gemini reasoning.
    /// </summary>
    private static PredictionResponse? BuildPredictionResponse(
        FixtureAnalysisResult analysis,
        GeminiAnalysis? gemini)
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
                Reason = !string.IsNullOrWhiteSpace(gemini?.Over25Summary)
                    ? gemini.Over25Summary
                    : d.Markets.Over25.Reason
            },
            BTTS = new BoolPrediction
            {
                Prediction = wp.BTTS,
                Probability = Math.Round(wp.BTTSProb, 2),
                IsQualified = d.Markets.BTTS.IsQualified,
                Reason = !string.IsNullOrWhiteSpace(gemini?.BttsSummary)
                    ? gemini.BttsSummary
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
                Reason = !string.IsNullOrWhiteSpace(gemini?.Under25Summary)
                    ? gemini.Under25Summary
                    : d.Markets.LowScoring.Reason
            },
            HomeWin = new BoolPrediction
            {
                Prediction = wp.MatchWinner.Equals("home", StringComparison.OrdinalIgnoreCase),
                Probability = wp.HomeProb,
                IsQualified = d.Markets.MatchWinner.IsQualified && wp.MatchWinner.Equals("home", StringComparison.OrdinalIgnoreCase),
                Reason = GetWinnerReason(gemini, "home", d.Markets.MatchWinner.Reason)
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
                Reason = GetWinnerReason(gemini, "away", d.Markets.MatchWinner.Reason)
            },
            MatchWinner = new StringPrediction
            {
                Prediction = wp.MatchWinner,
                Confidence = wp.Confidence,
                IsQualified = d.Markets.MatchWinner.IsQualified,
                Reason = GetWinnerReason(gemini, wp.MatchWinner, d.Markets.MatchWinner.Reason)
            }
        };
    }

    /// <summary>
    /// Validates match result for completed fixtures.
    /// </summary>
    private static MatchResult? ValidateMatchResult(Fixture fixture, FixtureAnalysisResult analysis)
    {
        if (fixture.Status != "FT")
            return null;

        var actualScore = $"{fixture.HomeGoal}:{fixture.AwayGoal}";

        if (analysis.Prediction == null)
            return new MatchResult { ActualScore = actualScore, IsCorrect = false };

        string predWinner = analysis.Prediction.MatchWinner;
        bool isCorrect =
            (predWinner.Equals("home", StringComparison.OrdinalIgnoreCase) && fixture.HomeGoal > fixture.AwayGoal) ||
            (predWinner.Equals("draw", StringComparison.OrdinalIgnoreCase) && fixture.HomeGoal == fixture.AwayGoal) ||
            (predWinner.Equals("away", StringComparison.OrdinalIgnoreCase) && fixture.HomeGoal < fixture.AwayGoal);

        return new MatchResult { ActualScore = actualScore, IsCorrect = isCorrect };
    }

    /// <summary>
    /// Gets winner prediction reason from Gemini or fallback.
    /// </summary>
    private static string GetWinnerReason(GeminiAnalysis? gemini, string winner, string defaultReason)
    {
        var w = winner.ToLowerInvariant();
        if (w == "home" && !string.IsNullOrWhiteSpace(gemini?.HomeWinSummary))
            return gemini.HomeWinSummary;
        if (w == "away" && !string.IsNullOrWhiteSpace(gemini?.AwayWinSummary))
            return gemini.AwayWinSummary;
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
