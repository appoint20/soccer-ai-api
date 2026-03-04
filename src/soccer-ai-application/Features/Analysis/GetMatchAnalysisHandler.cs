using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Helpers;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Analysis;
using SoccerAi.Application.Entities;

namespace SoccerAi.Application.Features.Analysis;

/// <summary>
/// Handles match analysis requests with comprehensive prediction pipeline.
///
/// Orchestrates analysis workflow:
/// 1. Fetches fixtures and team data for specified date
/// 2. Analyzes each fixture through statistical models (Poisson, MC, ML)
/// 3. Requests Gemini AI batch analysis for semantic reasoning
/// 4. Maps analysis to response DTOs with Gemini integration
/// 5. Calculates accuracy summary for completed matches
///
/// Refactored for orchestration only. Complex logic delegated to:
/// - FixtureQueryHelper (data loading)
/// - AnalysisResponseMapper (DTO mapping)
/// - IMatchAnalysisService (statistical analysis)
/// - IGeminiAnalysisService (AI reasoning)
/// </summary>
public class GetMatchAnalysisHandler(
    FixtureQueryHelper queryHelper,
    IMatchAnalysisService analysisService,
    ILogger<GetMatchAnalysisHandler> logger)
    : IRequestHandler<GetMatchAnalysisQuery, GetMatchAnalysisResponse>
{
    public async Task<GetMatchAnalysisResponse> Handle(
        IReceiveContext<GetMatchAnalysisQuery> context,
        CancellationToken cancellationToken)
    {
        var query = context.Message;
        var lang = query.Language ?? "en";
        var date = query.Date ?? DateTimeOffset.UtcNow;

        logger.LogInformation("Analyzing matches for {Date} (UTC) in {Lang}",
            date.ToString("yyyy-MM-dd"), lang);

        // Step 1: Load fixtures and teams
        var (fixtures, teams) = await queryHelper.GetFixturesWithTeamsAsync(date, cancellationToken);

        logger.LogInformation("Loaded {Count} fixtures from DB for {Date}", fixtures.Count, date.ToString("yyyy-MM-dd"));

        if (fixtures.Count == 0)
        {
            logger.LogWarning("No fixtures found in database for date {Date}", date.ToString("yyyy-MM-dd"));
            return new GetMatchAnalysisResponse { Matches = new(), Summary = null };
        }

        // Step 2: Run core analysis for all fixtures
        var fixtureAnalysisMap = new Dictionary<int, FixtureAnalysisResult>();
        int analysisCount = 0;
        foreach (var fixture in fixtures)
        {
            try 
            {
                var analysis = await analysisService.AnalyzeFixtureAsync(fixture, cancellationToken);
                fixtureAnalysisMap[fixture.Id] = analysis;
                analysisCount++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to analyze fixture {Id} ({Home} vs {Away})", 
                    fixture.Id, fixture.HomeTeamId, fixture.AwayTeamId);
            }
        }

        logger.LogInformation("Successfully analyzed {Count}/{Total} fixtures", analysisCount, fixtures.Count);

        // Step 3: Map to response DTOs
        var analysisList = new List<MatchAnalysis>();
        int skipMissingTeam = 0;
        int skipMissingAnalysis = 0;

        foreach (var fixture in fixtures)
        {
            try
            {
                if (!fixtureAnalysisMap.TryGetValue(fixture.Id, out var analysis))
                {
                    skipMissingAnalysis++;
                    continue;
                }

                var homeTeam = teams.GetValueOrDefault(fixture.HomeTeamId);
                var awayTeam = teams.GetValueOrDefault(fixture.AwayTeamId);

                if (homeTeam == null || awayTeam == null)
                {
                    skipMissingTeam++;
                    logger.LogWarning("Skipping fixture {Id} due to missing team data (Home: {HomeId} Found: {HomeFound}, Away: {AwayId} Found: {AwayFound})",
                        fixture.Id, fixture.HomeTeamId, homeTeam != null, fixture.AwayTeamId, awayTeam != null);
                    continue;
                }

                var matchAnalysis = AnalysisResponseMapper.MapToResponse(
                    fixture, analysis, homeTeam, awayTeam, analysis.Gemini);

                analysisList.Add(matchAnalysis);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error mapping fixture {Id} to response", fixture.Id);
            }
        }

        if (skipMissingAnalysis > 0 || skipMissingTeam > 0)
        {
            logger.LogWarning("Filtered out {AnalysisCount} matches due to analysis failure and {TeamCount} matches due to missing team metadata",
                skipMissingAnalysis, skipMissingTeam);
        }

        // Step 5: Calculate summary
        var summary = AnalysisResponseMapper.CalculateSummary(analysisList);

        logger.LogInformation("Returning {Count} matches in response", analysisList.Count);

        return new GetMatchAnalysisResponse
        {
            Matches = analysisList,
            Summary = analysisList.Count > 0 ? summary : null
        };
    }


}
