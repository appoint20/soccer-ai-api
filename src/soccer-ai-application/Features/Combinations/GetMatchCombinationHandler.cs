using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Helpers;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Combinations;

namespace SoccerAi.Application.Features.Combinations;

/// <summary>
/// Handles combination portfolio generation requests.
///
/// Orchestrates the combination pipeline:
/// 1. Fetches fixtures and team data for the specified date
/// 2. Analyzes all fixtures through statistical models
/// 3. Builds portfolio from qualified market candidates
/// 4. Returns combination recommendations with EV metrics
///
/// Refactored for single responsibility: orchestration only.
/// Portfolio building logic delegated to CombinationPortfolioBuilder.
/// Data loading delegated to FixtureQueryHelper.
/// </summary>
public class GetMatchCombinationHandler(
    FixtureQueryHelper queryHelper, IMatchAnalysisService analysisService,
    CombinationPortfolioBuilder portfolioBuilder, ILogger<GetMatchCombinationHandler> logger)
    : IRequestHandler<GetMatchCombinationQuery, GetMatchCombinationResponse>
{
    public async Task<GetMatchCombinationResponse> Handle(
        IReceiveContext<GetMatchCombinationQuery> context,
        CancellationToken cancellationToken)
    {
        var query = context.Message;
        logger.LogInformation("Generating combination for {Date}", query.Date.ToString("yyyy-MM-dd"));

        // Step 1: Load fixtures and teams
        var (fixtures, teams) = await queryHelper.GetFixturesWithTeamsAsync(
            query.Date,
            cancellationToken);

        logger.LogInformation("Loaded {Count} fixtures for {Date}", fixtures.Count, query.Date.ToString("yyyy-MM-dd"));

        if (fixtures.Count == 0)
        {
            logger.LogInformation("No fixtures found for date {Date}", query.Date.ToString("yyyy-MM-dd"));
            return new GetMatchCombinationResponse([]);
        }

        // Step 2: Analyze all fixtures
        var analysisMap = new Dictionary<int, FixtureAnalysisResult>();
        foreach (var fixture in fixtures)
        {
            var analysis = await analysisService.AnalyzeFixtureAsync(fixture, cancellationToken);
            analysisMap[fixture.Id] = analysis;
        }

        logger.LogInformation("Analyzed {Count} fixtures", analysisMap.Count);

        // Step 3: Build portfolio combinations
        var combinations = await portfolioBuilder.BuildPortfolioAsync(
            fixtures,
            teams,
            analysisMap,
            cancellationToken);

        logger.LogInformation("Generated {Count} combinations", combinations.Count);

        return new GetMatchCombinationResponse(combinations);
    }
}
