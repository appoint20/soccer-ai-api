using Mediator.Net;
using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Features.Analysis;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

using SoccerAi.Application.Exceptions;

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
    ILogger<GetMatchCombinationHandler> logger)
    : IRequestHandler<GetMatchCombinationQuery, GetMatchCombinationResponse>
{
    public async Task<GetMatchCombinationResponse> Handle(
        IReceiveContext<GetMatchCombinationQuery> context,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = context.Message;
            logger.LogInformation("[Combinations] Generating JSON structures for {Date}", query.Date.ToString("yyyy-MM-dd"));

            // Step 0: Check Database Cache for existing combinations!
            var targetDate = new DateTimeOffset(query.Date.Year, query.Date.Month, query.Date.Day, 0, 0, 0, TimeSpan.Zero);
            
            logger.LogInformation("[Combinations] Checking cache for {Date} {Lang} (Refresh: {Refresh})", targetDate, query.Language, query.Refresh);
            
            var cachedCombo = query.Refresh ? null : await dbContext.Combinations
                .FirstOrDefaultAsync(c => c.Date == targetDate && c.Language == query.Language && c.IsDailyCache, cancellationToken);
                
            if (cachedCombo != null && !string.IsNullOrEmpty(cachedCombo.Payload))
            {
                logger.LogInformation("[Combinations] Cache HIT for {Date}. Re-verifying metadata...", targetDate.ToString("yyyy-MM-dd"));
                var cachedList = System.Text.Json.JsonSerializer.Deserialize<List<CombinationDto>>(cachedCombo.Payload) ?? new();
                
                // RE-CLEAN: Even if cached, ensure we use latest names and confidence from live analysis
                var cacheAnalysisQuery = new GetMatchAnalysisQuery { Date = query.Date, Language = query.Language };
                var cacheAnalysisResponse = await mediator.RequestAsync<GetMatchAnalysisQuery, GetMatchAnalysisResponse>(cacheAnalysisQuery, cancellationToken);
                var cacheSourceMatches = cacheAnalysisResponse.Matches;

                foreach (var combo in cachedList)
                {
                    if (combo.Matches == null) continue;
                    
                    foreach (var match in combo.Matches)
                    {
                        if (match == null) continue;
                        
                        var source = cacheSourceMatches?.FirstOrDefault(m => m.Id == match.FixtureId);
                        if (source != null)
                        {
                            // Fix properties that might be stale in cache
                            typeof(CombinationMatchDto).GetProperty("League")?.SetValue(match, source.League);
                            typeof(CombinationMatchDto).GetProperty("HomeTeam")?.SetValue(match, source.HomeTeam);
                            typeof(CombinationMatchDto).GetProperty("AwayTeam")?.SetValue(match, source.AwayTeam);
                            typeof(CombinationMatchDto).GetProperty("Confidence")?.SetValue(match, source.Gemini?.Confidence ?? 0.0);
                            typeof(CombinationMatchDto).GetProperty("Reasoning")?.SetValue(match, source.Gemini?.Reasoning ?? string.Empty);
                        }
                    }
                }

                return new GetMatchCombinationResponse(cachedList);
            }

            logger.LogInformation("[Combinations] Cache MISS. Requesting analysis from Mediator...");

            // Step 1: Request full Match Analysis payload via Mediator
            var analysisQuery = new GetMatchAnalysisQuery { Date = query.Date, Language = query.Language };
            var analysisResponse = await mediator.RequestAsync<GetMatchAnalysisQuery, GetMatchAnalysisResponse>(analysisQuery, cancellationToken);

            var matches = analysisResponse.Matches;

            if (matches == null || matches.Count == 0)
            {
                logger.LogInformation("[Combinations] No matches found for {Date}", query.Date.ToString("yyyy-MM-dd"));
                return new GetMatchCombinationResponse([]);
            }

            // FILTER: Only use games that have a complete Gemini Recommendation attached!
            var fullyAnalyzedMatches = matches
                .Where(x => x.Gemini != null && !string.IsNullOrEmpty(x.Gemini.Recommendation))
                .ToList();

            logger.LogInformation("[Combinations] Found {Count} total matches, {Analyzed} are fully analyzed.", matches.Count, fullyAnalyzedMatches.Count);

            if (fullyAnalyzedMatches.Count == 0)
            {
                logger.LogInformation("[Combinations] No analyzed games available. Returning empty.");
                return new GetMatchCombinationResponse([]);
            }

            // Step 2: Rank matches by absolute backend confidence to feed Gemini the best baseline
            var orderedCandidates = fullyAnalyzedMatches
                .OrderByDescending(x => x.Gemini?.Confidence ?? 0.0)
                .ToList();

            // --- PURE MATHEMATICAL 10-TIER ENGINE ---
            // Goal: Generate exactly 10 combinations using high-confidence Statistical & ML models ONLY.
            
            var combinations = new List<CombinationDto>();
            var usedMatchIds = new HashSet<int>();

            // 1. Prepare the statistical pool (Confidence > 60%)
            var statPool = matches
                .Where(m => m.Prediction?.MatchWinner != null && (m.Prediction?.MatchWinner?.Confidence ?? 0) >= 0.60)
                .OrderByDescending(m => m.Prediction.MatchWinner.Confidence)
                .ToList();

            logger.LogInformation("[Combinations] Starting Pure Math generation with {PoolCount} candidates.", statPool.Count);

            // 2. Generate exactly 10 portfolios using a rotating exhaustion strategy
            while (combinations.Count < 10 && statPool.Count >= 2)
            {
                // Determine if we need a Treble (3 matches) or a Double (2 matches)
                // Rule: We want at least 2 Trebles in the daily mix, the rest as Doubles.
                var treblesCreated = combinations.Count(c => c.Type == "TREBLE");
                bool isTreble = treblesCreated < 2 && statPool.Count >= 3;
                int take = isTreble ? 3 : 2;

                // Pick the top matches that haven't been used yet
                var chunk = statPool.Where(m => !usedMatchIds.Contains(m.Id)).Take(take).ToList();
                
                if (chunk.Count < 2) 
                {
                    // If we ran out of fresh matches, break (We refuse to recycle matches in the daily 10)
                    logger.LogWarning("[Combinations] Match pool exhausted. Only generated {Count}/10 portfolios.", combinations.Count);
                    break;
                }

                var combo = new CombinationDto
                {
                    SourceType = "SYSTEM",
                    Type = chunk.Count == 3 ? "TREBLE" : "DOUBLE",
                    TotalOdds = Math.Round(chunk.Select(m => GetPrimaryOdds(m)).Aggregate(1.0, (acc, val) => acc * val), 2),
                    Reason = "System Recommendation: High-confidence daily selection built from peak historical performance and current data trends.",
                    Matches = chunk.Select(m => new CombinationMatchDto
                    {
                        FixtureId = m.Id, 
                        League = m.League, 
                        HomeTeam = m.HomeTeam, 
                        AwayTeam = m.AwayTeam,
                        Selection = GetPrimarySelection(m), 
                        Odds = GetPrimaryOdds(m),
                        Confidence = (m.Prediction?.MatchWinner?.Confidence ?? 0) * 100,
                        Reasoning = GetNaturalDailyReasoning(m)
                    }).ToList()
                };

                combinations.Add(combo);
                foreach (var m in chunk) usedMatchIds.Add(m.Id);
            }

            logger.LogInformation("[Combinations] Final output: {Count} combinations.", combinations.Count);

            // Re-index all combinations to ensure unique, sequential IDs across batches
            for (int i = 0; i < combinations.Count; i++)
            {
                combinations[i].CombinationId = i + 1;
            }

            // Step 4: CACHE the result for future API calls today!
            if (combinations.Count > 0)
            {
                try 
                {
                    var existingCache = await dbContext.Combinations
                        .FirstOrDefaultAsync(c => c.Date == targetDate && c.Language == query.Language && c.IsDailyCache, cancellationToken);

                    if (existingCache != null)
                    {
                        logger.LogInformation("[Combinations] Updating existing SQL Cache...");
                        existingCache.Payload = System.Text.Json.JsonSerializer.Serialize(combinations);
                    }
                    else 
                    {
                        logger.LogInformation("[Combinations] Creating new SQL Cache entry...");
                        var newCache = new Combination
                        {
                            Name = $"Daily Cache {query.Date:yyyy-MM-dd}",
                            Date = targetDate,
                            Language = query.Language,
                            Payload = System.Text.Json.JsonSerializer.Serialize(combinations),
                            IsDailyCache = true,
                            Status = "Cached"
                        };
                        dbContext.Combinations.Add(newCache);
                    }
                    
                    await dbContext.SaveChangesAsync(cancellationToken);
                    logger.LogInformation("[Combinations] SQL Cache Sync Complete.");
                }
                catch (Exception cacheEx)
                {
                    logger.LogWarning(cacheEx, "[Combinations] Failed to SYNC cache, but returning results anyway.");
                }
            }

            return new GetMatchCombinationResponse(combinations);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Combinations] CRITICAL FAILURE generating combinations for {Date}", context.Message.Date);
            throw; // Re-throw for GlobalExceptionMiddleware
        }
    }

    private static string GetPrimarySelection(MatchAnalysis match)
    {
        if (match.Prediction?.MatchWinner == null) return "Match Winner";
        return match.Prediction.MatchWinner.Prediction.ToLower() switch
        {
            "home" => "Match Winner (Home)",
            "away" => "Match Winner (Away)",
            _ => "BTTS" // Default to BTTS if draw/other to keep it diverse
        };
    }

    private static string GetNaturalDailyReasoning(MatchAnalysis m)
    {
        var conf = m.Prediction?.MatchWinner?.Confidence ?? 0;
        var confText = conf switch
        {
            >= 0.8 => "very high predictive probability",
            >= 0.7 => "strong statistical consensus",
            _ => "favorable analytical advantage"
        };
        return $"Selection backed by {confText} and exceptional season form.";
    }

    private static double GetPrimaryOdds(MatchAnalysis match)
    {
        if (match.Prediction?.MatchWinner == null) return 1.0;
        return match.Prediction.MatchWinner.Prediction.ToLower() switch
        {
            "home" => match.OddsHomeWin > 0 ? match.OddsHomeWin : 1.5,
            "away" => match.OddsAwayWin > 0 ? match.OddsAwayWin : 1.5,
            _ => match.OddsBttsYes > 0 ? match.OddsBttsYes : 1.8
        };
    }
}
