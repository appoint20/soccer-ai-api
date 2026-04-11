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
    IGeminiAnalysisService geminiAnalysisService,
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

            var combinations = new List<CombinationDto>();
            var usedMatchIds = new HashSet<int>();

            // Phase 1: Pure AI (3 Combinations)
            try 
            {
                var aiList = new List<CombinationDto>();
                foreach (var batch in orderedCandidates.Chunk(10))
                {
                    logger.LogInformation("[Combinations] Yielding {Count} matches to Gemini builder...", batch.Length);
                    var batchResults = await geminiAnalysisService.BuildCombinationsAsync(batch.ToList());
                    if (batchResults != null) aiList.AddRange(batchResults);
                    if (aiList.Count >= 3) break;
                }

                foreach (var combo in aiList.Take(3))
                {
                    combo.SourceType = "AI";
                    combinations.Add(combo);
                    foreach (var m in combo.Matches) usedMatchIds.Add(m.FixtureId);
                }
            }
            catch (Exception ex) when (ex is GeminiQuotaExceededException || ex.Message.Contains("quota") || ex.Message.Contains("429"))
            {
                logger.LogWarning("[Combinations] Gemini unavailable for AI Phase. Skipping to Fallback...");
            }

            // Phase 2: Hybrid (2 Combinations)
            // Strategy: 1 AI-analyzed match + 1 High-confidence Stat match
            if (combinations.Count < 5) 
            {
                var hybridCandidates = orderedCandidates.Where(m => !usedMatchIds.Contains(m.Id)).ToList();
                var statPool = matches.Where(m => !usedMatchIds.Contains(m.Id) && m.Prediction?.MatchWinner?.Confidence >= 0.70).ToList();

                for (int i = 0; i < 2; i++)
                {
                    if (hybridCandidates.Count == 0 || statPool.Count == 0) break;

                    var aiMatch = hybridCandidates[0];
                    var statMatch = statPool[0];

                    var combo = new CombinationDto
                    {
                        SourceType = "HYBRID",
                        Type = "DOUBLE",
                        TotalOdds = Math.Round(GetPrimaryOdds(aiMatch) * GetPrimaryOdds(statMatch), 2),
                        Reason = "Hybrid Synergy: Combining expert AI reasoning with high-confidence statistical probability.",
                        Matches = new List<CombinationMatchDto>
                        {
                            new() {
                                FixtureId = aiMatch.Id, League = aiMatch.League, HomeTeam = aiMatch.HomeTeam, AwayTeam = aiMatch.AwayTeam,
                                Selection = aiMatch.Gemini?.Recommendation ?? GetPrimarySelection(aiMatch),
                                Odds = GetPrimaryOdds(aiMatch), Confidence = (aiMatch.Gemini?.Confidence ?? 0),
                                Reasoning = aiMatch.Gemini?.Reasoning ?? "AI prioritized selection."
                            },
                            new() {
                                FixtureId = statMatch.Id, League = statMatch.League, HomeTeam = statMatch.HomeTeam, AwayTeam = statMatch.AwayTeam,
                                Selection = GetPrimarySelection(statMatch), Odds = GetPrimaryOdds(statMatch),
                                Confidence = (statMatch.Prediction?.MatchWinner?.Confidence ?? 0) * 100,
                                Reasoning = "High-confidence statistical advantage."
                            }
                        }
                    };
                    combinations.Add(combo);
                    usedMatchIds.Add(aiMatch.Id);
                    usedMatchIds.Add(statMatch.Id);
                    hybridCandidates.RemoveAt(0);
                    statPool.RemoveAt(0);
                }
            }

            // Phase 3: Mathematical (5 Combinations)
            var statOnlyPool = matches
                .Where(m => !usedMatchIds.Contains(m.Id) && m.Prediction?.MatchWinner?.Confidence >= 0.65)
                .OrderByDescending(m => m.Prediction?.MatchWinner?.Confidence ?? 0)
                .ToList();

            while (combinations.Count < 10 && statOnlyPool.Count >= 2)
            {
                var isTreble = combinations.Count == 5 || (combinations.Count < 5 && combinations.All(c => c.Matches.Count < 3));
                int take = (isTreble && statOnlyPool.Count >= 3) ? 3 : 2;
                
                var chunk = statOnlyPool.Take(take).ToList();
                if (chunk.Count < 2) break;

                var combo = new CombinationDto
                {
                    SourceType = "MATHEMATICAL",
                    Type = chunk.Count == 3 ? "TREBLE" : "DOUBLE",
                    TotalOdds = Math.Round(chunk.Select(m => GetPrimaryOdds(m)).Aggregate(1.0, (acc, val) => acc * val), 2),
                    Reason = "Pure Statistical Advantage: High mathematical consensus across Poisson and ML models.",
                    Matches = chunk.Select(m => new CombinationMatchDto
                    {
                        FixtureId = m.Id, League = m.League, HomeTeam = m.HomeTeam, AwayTeam = m.AwayTeam,
                        Selection = GetPrimarySelection(m), Odds = GetPrimaryOdds(m),
                        Confidence = (m.Prediction?.MatchWinner?.Confidence ?? 0) * 100,
                        Reasoning = "Data-driven statistical selection."
                    }).ToList()
                };
                combinations.Add(combo);
                foreach (var m in chunk) usedMatchIds.Add(m.Id);
                statOnlyPool.RemoveRange(0, take);
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
