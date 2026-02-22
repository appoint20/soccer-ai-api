using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Entities;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.Analysis;

public class GetMatchAnalysisHandler(
    IApplicationDbContext dbContext,
    IMatchAnalysisService analysisService,
    ILogger<GetMatchAnalysisHandler> logger) : IRequestHandler<GetMatchAnalysisQuery, GetMatchAnalysisResponse>
{
    public async Task<GetMatchAnalysisResponse> Handle(
        IReceiveContext<GetMatchAnalysisQuery> context, CancellationToken cancellationToken)
    {
        var query = context.Message;
        var date = query.Date.Date;
        var endOfDay = date.AddDays(1);

        logger.LogInformation("Analyzing matches for {Date}", date.ToString("yyyy-MM-dd"));

        var fixtures = await dbContext.Fixtures
            .Where(f => f.Date >= date && f.Date < endOfDay)
            .ToListAsync(cancellationToken);

        var teamIds = fixtures.SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId }).Distinct().ToList();
        var teams = await dbContext.Teams
            .Where(t => teamIds.Contains(t.ApiId))
            .ToDictionaryAsync(t => t.ApiId, t => t, cancellationToken);

        var analysisList = new List<MatchAnalysis>();

        foreach (var fixture in fixtures)
        {
            try
            {
                var homeTeam = teams.GetValueOrDefault(fixture.HomeTeamId);
                var awayTeam = teams.GetValueOrDefault(fixture.AwayTeamId);
                if (homeTeam == null || awayTeam == null) continue;

                // ── Single shared analysis pipeline ──
                var analysis = await analysisService.AnalyzeFixtureAsync(fixture, cancellationToken);

                // ── Build prediction response ──
                PredictionResponse? predictionResponse = null;
                if (analysis.Prediction != null)
                {
                    var wp = analysis.Prediction;
                    var d = analysis.Decisions;
                    predictionResponse = new PredictionResponse
                    {
                        Over25 = new BoolPrediction
                        {
                            Prediction = wp.Over25,
                            Probability = Math.Round(wp.Over25Prob, 2),
                            IsQualified = d.Markets.Over25.IsQualified,
                            Reason = d.Markets.Over25.Reason
                        },
                        BTTS = new BoolPrediction
                        {
                            Prediction = wp.BTTS,
                            Probability = Math.Round(wp.BTTSProb, 2),
                            IsQualified = d.Markets.BTTS.IsQualified,
                            Reason = d.Markets.BTTS.Reason
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
                            Reason = d.Markets.LowScoring.Reason
                        },
                        MatchWinner = new StringPrediction
                        {
                            Prediction = wp.MatchWinner,
                            Confidence = wp.Confidence,
                            IsQualified = d.Markets.MatchWinner.IsQualified,
                            Reason = d.Markets.MatchWinner.Reason
                        }
                    };
                }

                // ── Match result validation ──
                MatchResult? matchResult = null;
                if (fixture.Status == "FT" && analysis.Prediction != null)
                {
                    string predWinner = analysis.Prediction.MatchWinner;
                    bool isCorrect =
                        (predWinner.Equals("home", StringComparison.OrdinalIgnoreCase) && fixture.HomeGoal > fixture.AwayGoal) ||
                        (predWinner.Equals("draw", StringComparison.OrdinalIgnoreCase) && fixture.HomeGoal == fixture.AwayGoal) ||
                        (predWinner.Equals("away", StringComparison.OrdinalIgnoreCase) && fixture.HomeGoal < fixture.AwayGoal);

                    matchResult = new MatchResult
                    {
                        ActualScore = $"{fixture.HomeGoal}:{fixture.AwayGoal}",
                        IsCorrect = isCorrect
                    };
                }
                else if (fixture.Status == "FT")
                {
                    matchResult = new MatchResult
                    {
                        ActualScore = $"{fixture.HomeGoal}:{fixture.AwayGoal}",
                        IsCorrect = false
                    };
                }

                var aiRes = fixture.GeminiRecommendation != null ? new GeminiAnalysis
                {
                    Recommendation = fixture.GeminiRecommendation,
                    Confidence = fixture.GeminiConfidence ?? 0,
                    Reasoning = fixture.GeminiReasoning ?? "",
                    Analysis = fixture.GeminiAnalysis ?? "",
                    IsTrap = fixture.GeminiIsTrap ?? false
                } : null;

                analysis.TeamStats.Home.Rank = homeTeam.Rank;
                analysis.TeamStats.Home.Points = homeTeam.Points;
                analysis.TeamStats.Home.Form = homeTeam.Form;
                analysis.TeamStats.Home.FormPercentage = CalculateFormPercentage(homeTeam.Form);

                analysis.TeamStats.Away.Rank = awayTeam.Rank;
                analysis.TeamStats.Away.Points = awayTeam.Points;
                analysis.TeamStats.Away.Form = awayTeam.Form;
                analysis.TeamStats.Away.FormPercentage = CalculateFormPercentage(awayTeam.Form);

                var ma = new MatchAnalysis
                {
                    Id = fixture.Id,
                    Date = fixture.Date,
                    Time = fixture.Date.TimeOfDay,
                    League = analysis.LeagueName,
                    HomeTeam = homeTeam.Name,
                    AwayTeam = awayTeam.Name,
                    Result = matchResult,
                    OddsHomeWin = analysis.OddsHomeWin,
                    OddsDraw = analysis.OddsDraw,
                    OddsAwayWin = analysis.OddsAwayWin,
                    OddsOver25 = analysis.OddsOver25,
                    OddsBttsYes = analysis.OddsBttsYes,
                    HomeStats = analysis.TeamStats.Home,
                    AwayStats = analysis.TeamStats.Away,
                    Models = analysis.Models,
                    Prediction = predictionResponse,
                    Trap = analysis.Decisions.Trap,
                    H2H = analysis.H2H,
                    Gemini = aiRes
                };
                
                analysisList.Add(ma);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error analyzing fixture {Id}", fixture.Id);
            }
        }

        return new GetMatchAnalysisResponse { Matches = analysisList };
    }

    private static int CalculateFormPercentage(string form)
    {
        if (string.IsNullOrWhiteSpace(form)) return 0;
        int points = 0;
        foreach (var c in form.ToUpperInvariant())
        {
            if (c == 'W') points += 3;
            else if (c == 'D') points += 1;
        }
        return (int)Math.Round((points / (double)(form.Length * 3)) * 100);
    }
}
