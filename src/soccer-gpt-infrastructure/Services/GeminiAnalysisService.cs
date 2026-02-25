using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using soccer_gpt_application.Features.Combinations;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_infrastructure.Configuration;

namespace soccer_gpt_infrastructure.Services;

public class GeminiAnalysisService : IGeminiAnalysisService
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiAnalysisService> _logger;
    private readonly IMemoryCache _cache;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GeminiAnalysisService(
        HttpClient httpClient,
        IOptions<GeminiOptions> options,
        ILogger<GeminiAnalysisService> logger,
        IMemoryCache cache)
    {
        _httpClient = httpClient;
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _options.ApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? string.Empty;
        }
        _logger = logger;
        _cache = cache;
    }

    public async Task<Dictionary<int, GeminiAnalysis>> AnalyzeBatchAsync(List<GeminiBatchItem> items)
    {
        var result = new Dictionary<int, GeminiAnalysis>();
        if (items == null || items.Count == 0) return result;

        var itemsToFetch = new List<GeminiBatchItem>();

        foreach (var item in items)
        {
            var cacheKey = $"gemini_analysis_{item.FixtureId}";
            if (_cache.TryGetValue(cacheKey, out GeminiAnalysis cachedData))
            {
                result[item.FixtureId] = cachedData;
            }
            else
            {
                itemsToFetch.Add(item);
            }
        }

        if (itemsToFetch.Count == 0)
            return result;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("Gemini API Key missing.");
            return result;
        }

        foreach (var chunk in itemsToFetch.Chunk(15))
        {
            try
            {
                var payload = BuildRequestPayload(chunk.ToList());
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-pro-preview:generateContent?key={_options.ApiKey}";
                _logger.LogInformation("Sending batch of {Count} fixtures to Gemini 3.1 Pro", chunk.Count());
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                using var response = await _httpClient.PostAsJsonAsync(url, payload);
                stopwatch.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Gemini API error (took {Elapsed}ms): {Error}", stopwatch.ElapsedMilliseconds, error);
                    continue; // Continue to next chunk instead of returning
                }

                _logger.LogInformation("Gemini API success (took {Elapsed}ms)", stopwatch.ElapsedMilliseconds);

                var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiResponseDto>(JsonOptions);
                var jsonString = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

                if (string.IsNullOrWhiteSpace(jsonString))
                    continue;

                var parsed = JsonSerializer.Deserialize<List<GeminiMatchResponse>>(jsonString, JsonOptions);
                if (parsed == null) continue;

                foreach (var r in parsed)
                {
                    var analysis = new GeminiAnalysis
                    {
                        Recommendation = r.Recommendation,
                        Confidence = r.Confidence,
                        Reasoning = r.Reasoning,
                        IsTrap = r.IsTrap,
                        TrapReason = r.TrapReason,
                        OneLineSummary = r.OneLineSummary,
                        Analysis = r.Analysis,
                        BttsSummary = r.BttsSummary,
                        Over25Summary = r.Over25Summary,
                        Under25Summary = r.Under25Summary,
                        HomeWinSummary = r.HomeWinSummary,
                        AwayWinSummary = r.AwayWinSummary
                    };
                    result[r.FixtureId] = analysis;
                    _cache.Set($"gemini_analysis_{r.FixtureId}", analysis, TimeSpan.FromHours(24));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gemini analysis failed for a chunk");
            }
        }

        return result;
    }

    public async Task<List<CombinationDto>> BuildCombinationsAsync(List<CombinationMatchDto> candidates)
    {
        var result = new List<CombinationDto>();
        if (candidates == null || candidates.Count == 0 || string.IsNullOrWhiteSpace(_options.ApiKey))
            return result;

        var cacheKey = $"gemini_combinations_{string.Join("_", candidates.OrderBy(c => c.FixtureId).Select(c => c.FixtureId))}";
        
        if (_cache.TryGetValue(cacheKey, out List<CombinationDto>? cachedCombinations) && cachedCombinations != null)
        {
            _logger.LogInformation("Returning cached combinations for key: {CacheKey}", cacheKey);
            return cachedCombinations;
        }

        try
        {
            var prompt = BuildCombinationsPrompt(candidates);
            var payload = new GeminiRequest
            {
                Contents = new[]
                {
                    new GeminiContent
                    {
                        Parts = new[] { new GeminiPart { Text = prompt } }
                    }
                },
                GenerationConfig = new GeminiGenerationConfig
                {
                    Temperature = 0.2f,
                    ResponseMimeType = "application/json",
                    ResponseSchema = new
                    {
                        type = "ARRAY",
                        items = new
                        {
                            type = "OBJECT",
                            properties = new
                            {
                                name = new { type = "STRING", description = "Name of the parlay combination, e.g. 'High Conf Goal Double'" },
                                fixture_ids = new { type = "ARRAY", items = new { type = "INTEGER" } }
                            },
                            required = new[] { "name", "fixture_ids" }
                        }
                    }
                }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-pro-preview:generateContent?key={_options.ApiKey}";

            using var response = await _httpClient.PostAsJsonAsync(url, payload);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Gemini combinations API error: {Error}", error);
                return result;
            }

            var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiResponseDto>(JsonOptions);
            var jsonString = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

            if (string.IsNullOrWhiteSpace(jsonString))
                return result;

            var parsed = JsonSerializer.Deserialize<List<GeminiCombinationResponse>>(jsonString, JsonOptions);
            if (parsed == null) return result;

            foreach (var r in parsed)
            {
                var comboMatches = new List<CombinationMatchDto>();
                foreach (var id in r.FixtureIds)
                {
                    var match = candidates.FirstOrDefault(c => c.FixtureId == id);
                    if (match != null) comboMatches.Add(match);
                }
                
                if (comboMatches.Count > 0)
                {
                    result.Add(new CombinationDto(r.Name, comboMatches));
                }
            }
            
            if (result.Count > 0)
            {
                _cache.Set(cacheKey, result, TimeSpan.FromHours(12));
                _logger.LogInformation("Cached {Count} combinations for {CacheKey}", result.Count, cacheKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini combinations failed");
        }

        return result;
    }

    private GeminiRequest BuildRequestPayload(List<GeminiBatchItem> items)
    {
        return new GeminiRequest
        {
            Contents = new[]
            {
                new GeminiContent
                {
                    Role = "user",
                    Parts = new[]
                    {
                        new GeminiPart { Text = BuildSystemPrompt() },
                        new GeminiPart { Text = BuildMatchPrompt(items) }
                    }
                }
            },
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = 0.05f,
                ResponseMimeType = "application/json",
                ResponseSchema = GetResponseSchema()
            }
        };
    }
    
    private string BuildSystemPrompt()
    {
        return """
               You are a professional football betting risk analyst working inside a quantitative prediction system.

               Your role is NOT to predict results.
               Your role is to audit model predictions using match context and detect traps.

               STRICT OBJECTIVES:
               1. Validate if model probabilities are realistic.
               2. Detect mismatches between team quality.
               3. Detect low scoring risk.
               4. Detect class gap blowouts.
               5. Detect weak attacking teams.
               6. Detect unreliable lower league games.
               7. Return one final recommendation.

               DECISION RULES:

               Choose ONLY ONE recommendation:
               - "BTTS"
               - "Over 2.5 Goals"
               - "Under 2.5 Goals"
               - "Match Winner (Home)"
               - "Match Winner (Away)"
               - "Avoid"

               WHEN TO MARK AS A TRAP:
               - strong vs weak team mismatch
               - one team very low scoring
               - high clean sheet opponent
               - low league volatility / low quality league
               - inflated model probability
               - defensive teams
               - inconsistent form
               *IMPORTANT: If a trap is detected, you MUST explicitly state the exact cause of the trap and explain why it is misleading early in your Analysis.*

               LOW SCORING SIGNALS:
               - both teams scoring < 1.2 avg
               - high clean sheet %
               - low total goal expectation

               BTTS REQUIREMENTS:
               - both teams must realistically score
               - reject if one team weak attack

               CONFIDENCE SCALE:
               0–100 based on data clarity.

               REASONING:
               Must contain at least 2 to 4 key factors for each match. Keep it concise but detailed.
               
               ANALYSIS
               6-8 sentences of deep dive analysis on the upcoming match. Do not use prediction ml or poisson or technical words. Simple straightforward sentences which give a deep and thorough analysis on goals and wins. If a trap is detected, the exact cause must be explained clearly.

               ONE-LINE SUMMARIES:
               Provide user-friendly, one-line sentences that summarize the outlook for EACH of these specific markets, regardless of the final recommendation. These will be used for tooltip-style insights in the UI:
               - BTTS Summary: (e.g., "Both teams possess high-scoring forwards, suggesting a likely exchange of goals.")
               - Over 2.5 Summary: (e.g., "Historical high-scoring trends for these sides point toward a 3+ goal outcome.")
               - Under 2.5 Summary: (e.g., "Strong defensive setups on both sides often lead to low-scoring affairs.")
               - Home Win Summary: (e.g., "Home advantage and superior squad depth favor a solid home result.")
               - Away Win Summary: (e.g., "A clinical away counter-attacking style could catch the hosts off-guard.")

               Return ONLY valid JSON matching the schema.
               """;
    }
    
    private string BuildMatchPrompt(List<GeminiBatchItem> items)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("MATCH DATA:");

        foreach (var m in items)
        {
            var h = m.HomeStats;
            var a = m.AwayStats;
            var p = m.Prediction;

            sb.AppendLine($"""
                           FixtureId: {m.FixtureId}
                           Match: {m.HomeTeam} vs {m.AwayTeam}
                           League: {m.League}

                           Home Team:
                           - Goals Scored Avg: {h.AvgGoalsScoredLast7:F2}
                           - Goals Conceded Avg: {h.AvgGoalsConcededLast7:F2}
                           - Clean Sheet Rate: {h.CleanSheetRate:P0}

                           Away Team:
                           - Goals Scored Avg: {a.AvgGoalsScoredLast7:F2}
                           - Goals Conceded Avg: {a.AvgGoalsConcededLast7:F2}
                           - Clean Sheet Rate: {a.CleanSheetRate:P0}

                           Model Context:
                           - Winner: {p?.MatchWinner} (Confidence {p?.Confidence:F2})
                           - Over25 Probability: {p?.Over25Prob:F2}
                           - BTTS Probability: {p?.BTTSProb:F2}

                           ---
                           """);
        }

        return sb.ToString();
    }
    
    private string BuildCombinationsPrompt(List<CombinationMatchDto> candidates)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are an expert sports betting portfolio manager.");
        sb.AppendLine("Below is a list of highly filtered algorithmically identified value bets for today.");
        sb.AppendLine("Your goal is to construct exactly 4 distinctly different professional parlay combinations from this list.");
        sb.AppendLine("Rules:");
        sb.AppendLine("1. Build logical groupings with names like 'Safe Double', 'High Value Treble', 'Mixed Goals Portfolio', 'Elite Triple'.");
        sb.AppendLine("2. STRICT RULE: A single match (Fixture ID) can ONLY appear in ONE combination. DO NOT reuse the same match across multiple combinations.");
        sb.AppendLine("3. Each combination MUST have either exactly 2 or 3 legs. (Give at least one 3-leg combination).");
        sb.AppendLine("4. Avoid correlating matches from the same league if possible.");
        sb.AppendLine("5. Output ONLY the valid JSON structure matching the schema.");
        sb.AppendLine("\nCANDIDATES:");
        
        foreach(var c in candidates)
        {
            sb.AppendLine($"- ID: {c.FixtureId} | {c.HomeTeam} vs {c.AwayTeam}");
            sb.AppendLine($"  League: {c.LeagueName}");
            sb.AppendLine($"  Selection: {c.Market} ({c.Prediction}) @ Odds: {c.Odds:F2}");
            sb.AppendLine($"  Confidence: {c.Confidence}% | Expected Value: {c.ExpectedValue:F3}");
            sb.AppendLine($"  Gemini Input Analysis: {c.GeminiRecommendation} | {c.GeminiOneLineSummary}");
            sb.AppendLine("---");
        }
        
        return sb.ToString();
    }
    
    private object GetResponseSchema()
    {
        return new
        {
            type = "ARRAY",
            items = new
            {
                type = "OBJECT",
                properties = new
                {
                    fixtureId = new { type = "INTEGER" },
                    recommendation = new { type = "STRING" },
                    confidence = new { type = "NUMBER" },
                    reasoning = new { type = "STRING" },
                    isTrap = new { type = "BOOLEAN" },
                    trapReason = new { type = "STRING", description = "The explicit reason why this match is a trap (if any). Leave empty if not a trap." },
                    oneLineSummary = new { type = "STRING", description = "A general one-line user-friendly summary of the overall recommendation." },
                    bttsSummary = new { type = "STRING", description = "One-line analysis specifically for the BTTS market." },
                    over25Summary = new { type = "STRING", description = "One-line analysis specifically for the Over 2.5 goals market." },
                    under25Summary = new { type = "STRING", description = "One-line analysis specifically for the Under 2.5 goals market." },
                    homeWinSummary = new { type = "STRING", description = "One-line analysis specifically for a Home Win outcome." },
                    awayWinSummary = new { type = "STRING", description = "One-line analysis specifically for an Away Win outcome." },
                    analysis = new { type = "STRING" }
                },
                required = new[]
                {
                    "fixtureId",
                    "recommendation",
                    "confidence",
                    "reasoning",
                    "isTrap",
                    "trapReason",
                    "oneLineSummary",
                    "bttsSummary",
                    "over25Summary",
                    "under25Summary",
                    "homeWinSummary",
                    "awayWinSummary",
                    "analysis"
                }
            }
        };
    }
    
    private class GeminiRequest
    {
        [JsonPropertyName("contents")]
        public GeminiContent[] Contents { get; set; } = Array.Empty<GeminiContent>();

        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig? GenerationConfig { get; set; }
    }

    private class GeminiContent
    {
        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("parts")]
        public GeminiPart[] Parts { get; set; } = Array.Empty<GeminiPart>();
    }

    private class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private class GeminiGenerationConfig
    {
        [JsonPropertyName("temperature")]
        public float? Temperature { get; set; }

        [JsonPropertyName("responseMimeType")]
        public string? ResponseMimeType { get; set; }

        [JsonPropertyName("responseSchema")]
        public object? ResponseSchema { get; set; }
    }

    private class GeminiResponseDto
    {
        [JsonPropertyName("candidates")]
        public List<Candidate> Candidates { get; set; } = new();
    }

    private class Candidate
    {
        [JsonPropertyName("content")]
        public Content Content { get; set; } = new();
    }

    private class Content
    {
        [JsonPropertyName("parts")]
        public List<Part> Parts { get; set; } = new();
    }

    private class Part
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;
    }

    private class GeminiMatchResponse
    {
        public int FixtureId { get; set; }
        public string Recommendation { get; set; } = "";
        public double Confidence { get; set; }
        public string Reasoning { get; set; } = "";
        public string Analysis { get; set; } = "";
        public bool IsTrap { get; set; }
        public string TrapReason { get; set; } = "";
        public string OneLineSummary { get; set; } = "";
        public string BttsSummary { get; set; } = "";
        public string Over25Summary { get; set; } = "";
        public string Under25Summary { get; set; } = "";
        public string HomeWinSummary { get; set; } = "";
        public string AwayWinSummary { get; set; } = "";
    }

    private class GeminiCombinationResponse
    {
        public string Name { get; set; } = "";
        public List<int> FixtureIds { get; set; } = new();
    }
}
