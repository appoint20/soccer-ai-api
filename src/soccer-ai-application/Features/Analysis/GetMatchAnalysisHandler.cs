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
/// 3. Requests AI batch analysis for semantic reasoning
/// 4. Maps analysis to response DTOs with AI integration
/// 5. Calculates accuracy summary for completed matches
///
/// Refactored for orchestration only. Complex logic delegated to:
/// - FixtureQueryHelper (data loading)
/// - AnalysisResponseMapper (DTO mapping)
/// - IMatchAnalysisService (statistical analysis)
/// - IAiAnalysisService (AI reasoning)
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
        var (fixtures, teams, totalCount) = await queryHelper.GetFixturesWithTeamsAsync(date, query.Page, query.PageSize, query.OnlyAnalyzed, cancellationToken);

        logger.LogInformation("Loaded {Count} fixtures from DB for {Date} (Page: {Page}, PageSize: {PageSize})", 
            fixtures.Count, date.ToString("yyyy-MM-dd"), query.Page, query.PageSize);

        if (fixtures.Count == 0)
        {
            logger.LogWarning("No fixtures found in database for date {Date}", date.ToString("yyyy-MM-dd"));
            return new GetMatchAnalysisResponse 
            { 
                Matches = new(), 
                TotalCount = totalCount,
                Summary = new AnalysisSummary { TotalMatches = 0, CorrectMatches = 0, AccuracyRate = 0 } 
            };
        }

        // ... (Step 2-4 remains mostly the same, analysis is now limited to the returned fixtures) ...
        var fixtureAnalysisMap = new Dictionary<int, FixtureAnalysisResult>();
        int analysisCount = 0;
        foreach (var fixture in fixtures)
        {
            try 
            {
                var analysis = await analysisService.AnalyzeFixtureAsync(fixture, lang, cancellationToken);
                fixtureAnalysisMap[fixture.Id] = analysis;
                analysisCount++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to analyze fixture {Id} ({Home} vs {Away})", 
                    fixture.Id, fixture.HomeTeamId, fixture.AwayTeamId);
            }
        }

        var analysisList = new List<MatchAnalysis>();
        foreach (var fixture in fixtures)
        {
            if (!fixtureAnalysisMap.TryGetValue(fixture.Id, out var analysis)) continue;

            var homeTeam = teams.GetValueOrDefault(fixture.HomeTeamId);
            var awayTeam = teams.GetValueOrDefault(fixture.AwayTeamId);

            if (homeTeam == null || awayTeam == null) continue;

            var matchAnalysis = AnalysisResponseMapper.MapToResponse(
                fixture, analysis, homeTeam, awayTeam, analysis.Ai);

            analysisList.Add(matchAnalysis);
        }

        // Step 5: Calculate summary
        var summary = AnalysisResponseMapper.CalculateSummary(analysisList);

        return new GetMatchAnalysisResponse
        {
            Matches = analysisList,
            TotalCount = totalCount,
            Summary = summary
        };
    }
}
