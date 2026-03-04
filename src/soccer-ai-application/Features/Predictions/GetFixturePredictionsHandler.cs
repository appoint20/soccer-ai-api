using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Application.Features.Predictions;

/// <summary>
/// Handler for GetFixturePredictionsQuery using ML models.
/// </summary>
public class GetFixturePredictionsHandler(
    IApplicationDbContext dbContext,
    IMlPredictionService mlService,
    IFeatureExtractionService featureService,
    ILogger<GetFixturePredictionsHandler> logger) 
    : IRequestHandler<GetFixturePredictionsQuery, GetFixturePredictionsResponse>
{
    public async Task<GetFixturePredictionsResponse> Handle(
        IReceiveContext<GetFixturePredictionsQuery> context, 
        CancellationToken cancellationToken)
    {
        var query = context.Message;
        logger.LogInformation("Getting predictions for league {LeagueId} on {Date}", 
            query.LeagueId, query.Date.ToString("yyyy-MM-dd"));

        // Get upcoming fixtures for the date
        var startOfDay = query.Date.Date;
        var endOfDay = startOfDay.AddDays(1);

        var fixtures = await dbContext.Fixtures
            .Where(f => f.Date >= startOfDay
                && f.Date < endOfDay)
            .ToListAsync(cancellationToken);

        logger.LogInformation("Found {Count} fixtures (NS and FT)", fixtures.Count);

        var teamIds = fixtures.SelectMany(f => new[] { f.HomeTeamId, f.AwayTeamId }).Distinct().ToList();
        var teams = await dbContext.Teams
            .Where(t => teamIds.Contains(t.ApiId))
            .ToDictionaryAsync(t => t.ApiId, t => t.Name, cancellationToken);

        var predictions = new List<FixturePredictionDto>();

        foreach (var fixture in fixtures)
        {
            try
            {
                // Build feature array from fixture data
                var features = await featureService.BuildFeaturesAsync(fixture, cancellationToken);
                
                // Get ML predictions
                var mlResults = await mlService.PredictFromFeaturesAsync(features, cancellationToken);
                
                if (mlResults.Count == 0)
                {
                    logger.LogWarning("No ML predictions for fixture {FixtureId}", fixture.Id);
                    continue;
                }

                var prediction = new FixturePredictionDto(
                    MatchDate: fixture.Date,
                    Status: fixture.Status,
                    HomeTeamName: teams.GetValueOrDefault(fixture.HomeTeamId, $"Team {fixture.HomeTeamId}"),
                    AwayTeamName: teams.GetValueOrDefault(fixture.AwayTeamId, $"Team {fixture.AwayTeamId}"),
                    ActualHomeGoals: fixture.Status == "FT" ? fixture.HomeGoal : null,
                    ActualAwayGoals: fixture.Status == "FT" ? fixture.AwayGoal : null,
                    Over25: BuildMarketPrediction(mlResults.GetValueOrDefault("over25")),
                    Btts: BuildMarketPrediction(mlResults.GetValueOrDefault("btts")),
                    Goals2To3: BuildMarketPrediction(mlResults.GetValueOrDefault("goals_2_3")),
                    Hda: BuildHdaPrediction(mlResults.GetValueOrDefault("hda"))
                );

                predictions.Add(prediction);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error predicting fixture {FixtureId}", fixture.Id);
            }
        }

        return new GetFixturePredictionsResponse(
            date: query.Date,
            leagueId: query.LeagueId,
            predictions: predictions
        );
    }
    
    private static MarketPredictionDto BuildMarketPrediction(double[]? probs)
    {
        if (probs == null || probs.Length < 2)
            return new MarketPredictionDto(false, 0.0);

        var yesProb = probs.Length > 1 ? probs[1] : probs[0];
        var prediction = yesProb > 0.5;
        var confidence = prediction ? yesProb : (1 - yesProb);

        return new MarketPredictionDto(prediction, confidence);
    }

    private static HdaPredictionDto BuildHdaPrediction(double[]? probs)
    {
        if (probs == null || probs.Length < 3)
            return new HdaPredictionDto("Unknown", 0.33, 0.33, 0.33, 0.33);

        var predictions = new[] { "Home", "Draw", "Away" };
        var maxIdx = Array.IndexOf(probs, probs.Max());
        var confidence = probs[maxIdx];

        return new HdaPredictionDto(
            Prediction: predictions[maxIdx],
            Confidence: confidence,
            HomeProbability: probs[0],
            DrawProbability: probs[1],
            AwayProbability: probs[2]
        );
    }
}
