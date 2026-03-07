using Mediator.Net;
using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Features.Analysis;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Features.Combinations;

/// <summary>
/// Handles combination portfolio generation requests directly from JSON Analysis payloads.
///
/// Orchestrates the combination pipeline:
/// 1. Fetches full Match Analysis JSON objects via Mediator GetMatchAnalysisQuery 
/// 2. Ranks matches by highest overall internal confidence 
/// 3. Batches matches in groupings of 10 and yields straight to Gemini AI
/// 4. Returns structured combination DTOs combining the raw JSON elements
///
/// Refactored to bypass mathematical portfolio generators.
/// </summary>
public class GetMatchCombinationHandler(
    IMediator mediator,
    IApplicationDbContext dbContext,
    IGeminiAnalysisService geminiAnalysisService,
    ILogger<GetMatchCombinationHandler> logger)
    : IRequestHandler<GetMatchCombinationQuery, GetMatchCombinationResponse>
{
    public async Task<GetMatchCombinationResponse> Handle(
        IReceiveContext<GetMatchCombinationQuery> context,
        CancellationToken cancellationToken)
    {
        var query = context.Message;
        logger.LogInformation("Generating JSON combination structures for {Date}", query.Date.ToString("yyyy-MM-dd"));

        // Step 0: Check Database Cache for existing combinations!
        var targetDate = new DateTimeOffset(query.Date.Year, query.Date.Month, query.Date.Day, 0, 0, 0, TimeSpan.Zero);
        
        var cachedCombo = await dbContext.DailyCombinations
            .FirstOrDefaultAsync(c => c.Date == targetDate && c.Language == query.Language, cancellationToken);
            
        if (cachedCombo != null && !string.IsNullOrEmpty(cachedCombo.Payload))
        {
            logger.LogInformation("Found Cached Gemini Combinations for {Date}. Skipping Gemini API call.", targetDate.ToString("yyyy-MM-dd"));
            var cachedList = System.Text.Json.JsonSerializer.Deserialize<List<CombinationDto>>(cachedCombo.Payload);
            return new GetMatchCombinationResponse(cachedList ?? []);
        }

        // Step 1: Request full Match Analysis payload via Mediator
        var analysisQuery = new GetMatchAnalysisQuery { Date = query.Date, Language = query.Language };
        var analysisResponse = await mediator.RequestAsync<GetMatchAnalysisQuery, GetMatchAnalysisResponse>(analysisQuery, cancellationToken);

        var matches = analysisResponse.Matches;

        if (matches == null || matches.Count == 0)
        {
            logger.LogInformation("No analyzed matches found for date {Date}", query.Date.ToString("yyyy-MM-dd"));
            return new GetMatchCombinationResponse([]);
        }

        // FILTER: Only use games that have a complete Gemini Analysis attached!
        var fullyAnalyzedMatches = matches
            .Where(x => x.Gemini != null && !string.IsNullOrEmpty(x.Gemini.Recommendation))
            .ToList();

        if (fullyAnalyzedMatches.Count == 0)
        {
            logger.LogInformation("No games have finished Gemini Sync for date {Date}. Returning empty combinations.", query.Date.ToString("yyyy-MM-dd"));
            return new GetMatchCombinationResponse([]);
        }

        // Step 2: Rank matches by absolute backend confidence to feed Gemini the best baseline
        var orderedCandidates = fullyAnalyzedMatches
            .OrderByDescending(x => x.Gemini?.Confidence ?? 0.0)
            .ToList();

        var combinations = new List<CombinationDto>();

        // Step 3: Segment into dynamic API batches of 10 matches
        foreach (var batch in orderedCandidates.Chunk(10))
        {
            logger.LogInformation("Yielding {Count} fully-analyzed MatchAnalysis JSON elements to Gemini Combos.", batch.Length);
            var batchCombinations = await geminiAnalysisService.BuildCombinationsAsync(batch.ToList());
            
            if (batchCombinations != null && batchCombinations.Any())
            {
                combinations.AddRange(batchCombinations);
            }
        }

        logger.LogInformation("Successfully generated {Count} dynamic combinations via Gemini JSON logic.", combinations.Count);

        // Step 4: CACHE the result for future API calls today!
        if (combinations.Count > 0)
        {
            var newCache = new DailyCombination
            {
                Date = targetDate,
                Language = query.Language,
                Payload = System.Text.Json.JsonSerializer.Serialize(combinations)
            };
            
            dbContext.DailyCombinations.Add(newCache);
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Successfully saved Combinations schema into SQLite Database Cache.");
        }

        return new GetMatchCombinationResponse(combinations);
    }
}
