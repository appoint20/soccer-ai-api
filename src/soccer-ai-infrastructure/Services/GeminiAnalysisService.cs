using Google.GenAI;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Features.Combinations;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using Google.GenAI.Types;
using SoccerAi.Infrastructure.Options;
using SoccerAi.Application.Exceptions;

namespace SoccerAi.Infrastructure.Services;

public sealed class GeminiAnalysisService : IGeminiAnalysisService
{

    private const int BatchSize = 5;


    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiAnalysisService> _logger;
    private readonly Client _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GeminiAnalysisService(
        IOptions<GeminiOptions> options,
        ILogger<GeminiAnalysisService> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            _options.ApiKey = System.Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";

        _client = new Client(apiKey: _options.ApiKey, httpOptions: new HttpOptions
        {
            Timeout = 300000
        });
    }

    // ========================= ANALYSIS =========================

    public async Task<Dictionary<int, GeminiBilingualResult>> AnalyzeBatchAsync(List<GeminiBatchItem> items)
    {
        if (items == null || items.Count == 0 || !HasApiKey())
            return new();

        var result = new Dictionary<int, GeminiBilingualResult>();
        
        // Group by league for better context
        var leagues = items.GroupBy(x => x.League);

        foreach (var leagueGroup in leagues)
        {
            var leagueItems = leagueGroup.ToList();
            
            foreach (var chunk in leagueItems.Chunk(BatchSize))
            {
                var payload = BuildSystemPrompt() + "\n\n" + BuildMatchPrompt(chunk.ToList());
                var parsed = await ExecuteGeminiRequest<List<GeminiBilingualResult>>(payload, GetResponseSchema());

                if (parsed == null) continue;

                foreach (var r in parsed)
                {
                    result[r.FixtureId] = r;
                }
            }
        }

        return result;
    }


    // ========================= COMBINATIONS =========================

    public async Task<List<CombinationDto>> BuildCombinationsAsync(List<MatchAnalysis> candidates)
    {
        if (candidates == null || candidates.Count == 0 || !HasApiKey())
            return new();

        var parsed = await ExecuteGeminiRequest<List<GeminiCombinationResponse>>(
            BuildCombinationsPrompt(candidates), GetCombinationsSchema());

        if (parsed == null)
            return new();

        var validated = ValidateAndBuildCombinations(parsed, candidates);

        return validated;
    }

    // ========================= GEMINI CALL =========================

    private async Task<T?> ExecuteGeminiRequest<T>(string prompt, object? schema = null)
    {
        const int maxRetries = 3;
        var delayMs = 2000;

        for (var i = 0; i < maxRetries; i++)
        {
            Schema? geminiSchema = null;
            try
            {
                if (schema != null)
                {
                    // Convert anonymous object schema to the internal Schema type
                    var json = JsonSerializer.Serialize(schema);
                    geminiSchema = JsonSerializer.Deserialize<Schema>(json);
                }

                var config = new GenerateContentConfig
                {
                    Temperature = 0.05f,
                    ResponseMimeType = "application/json",
                    ResponseSchema = geminiSchema
                };

                var modelName = _options.Model;
                if (string.IsNullOrEmpty(modelName))
                    modelName = GetModelFromUrl(_options.BaseUrl);
                
                if (string.IsNullOrEmpty(modelName)) 
                    modelName = "gemini-1.5-flash"; // Ultimate fallback
                
                var response = await _client.Models.GenerateContentAsync(
                    modelName, 
                    prompt,
                    config);
                
                var text = response.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
                
                // Remove Markdown ```json headers if Gemini returns them
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (text.StartsWith("```json"))
                        text = text.Substring(7);
                    if (text.EndsWith("```"))
                        text = text.Substring(0, text.Length - 3);
                    if (text.EndsWith("```\n"))
                        text = text.Substring(0, text.Length - 4);
                }

                if (string.IsNullOrWhiteSpace(text)) return default;

                try
                {
                    return JsonSerializer.Deserialize<T>(text, JsonOptions);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize Gemini response: {Text}", text);
                    return default;
                }
            }
            catch (Exception ex)
            {
                // DETECT QUOTA EXCEEDED (HTTP 429)
                if (ex.Message.Contains("quota") || ex.Message.Contains("429") || (ex.InnerException?.Message.Contains("429") ?? false))
                {
                    var currentModel = _options.Model ?? GetModelFromUrl(_options.BaseUrl) ?? "gemini-3.1-pro";
                    
                    if (currentModel != "gemini-1.5-flash")
                    {
                        _logger.LogWarning("Gemini Primary Model ({Model}) Quota Exceeded. Falling back to gemini-1.5-flash...", currentModel);
                        
                        // Override model for this retry loop
                        var configFallback = new GenerateContentConfig
                        {
                            Temperature = 0.05f,
                            ResponseMimeType = "application/json",
                            ResponseSchema = geminiSchema
                        };

                        try 
                        {
                            var fallbackResponse = await _client.Models.GenerateContentAsync(
                                "gemini-1.5-flash", 
                                prompt,
                                configFallback);

                            var fallbackText = fallbackResponse.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
                            if (string.IsNullOrWhiteSpace(fallbackText)) return default;

                            // Clean and deserialize just like above
                            if (fallbackText.StartsWith("```json")) fallbackText = fallbackText.Substring(7);
                            if (fallbackText.EndsWith("```")) fallbackText = fallbackText.Substring(0, fallbackText.Length - 3);
                            if (fallbackText.EndsWith("```\n")) fallbackText = fallbackText.Substring(0, fallbackText.Length - 4);

                            return JsonSerializer.Deserialize<T>(fallbackText, JsonOptions);
                        }
                        catch (Exception fallbackEx)
                        {
                            _logger.LogCritical(fallbackEx, "Gemini FALLBACK Model (Flash) also failed or hit quota.");
                            throw new GeminiQuotaExceededException("Both primary and fallback Gemini models reached quota.", fallbackEx);
                        }
                    }

                    _logger.LogCritical("Gemini Quota Exceeded! Specific error: {Msg}", ex.Message);
                    throw new GeminiQuotaExceededException("Gemini daily quota reached. Please wait for reset or upgrade plan.", ex);
                }

                _logger.LogError(ex, "Gemini request failed (Attempt {Attempt})", i + 1);
                if (i < maxRetries - 1)
                {
                    await Task.Delay(delayMs);
                    delayMs *= 2;
                    continue;
                }
                return default;
            }
        }
        return default;
    }

    // ========================= VALIDATION =========================

    private List<CombinationDto> ValidateAndBuildCombinations(
        List<GeminiCombinationResponse> raw,
        List<MatchAnalysis> candidates)
    {
        var usedFixtures = new HashSet<int>();
        var result = new List<CombinationDto>();
        var candidateIds = candidates.Select(c => c.Id).ToHashSet();

        foreach (var combo in raw)
        {
            if (combo.Matches is null || combo.Matches.Count is < 2 or > 3)
                continue;

            var fixtureIds = combo.Matches.Select(m => m.FixtureId).ToList();

            if (fixtureIds.Any(id => usedFixtures.Contains(id)))
                continue;

            // Ensure all fixture IDs exist in the original candidate batch
            if (!fixtureIds.All(id => candidateIds.Contains(id)))
                continue;

            foreach (var id in fixtureIds)
                usedFixtures.Add(id);

            result.Add(new CombinationDto
            {
                CombinationId = combo.CombinationId,
                Type = combo.Type,
                TotalOdds = Math.Round(combo.TotalOdds, 2),
                Matches = combo.Matches.Select(m => new CombinationMatchDto
                {
                    FixtureId = m.FixtureId,
                    League = m.League,
                    HomeTeam = m.HomeTeam,
                    AwayTeam = m.AwayTeam,
                    Selection = m.Selection,
                    Odds = m.Odds
                }).ToList(),
                Reason = combo.Reason
            });
        }

        return result;
    }

    // ========================= PROMPTS =========================
private static string BuildCombinationsPrompt(List<MatchAnalysis> candidates)
{
    var sb = new StringBuilder();

    sb.AppendLine("""
You are a professional football betting portfolio optimizer.

Your job is to construct SAFE accumulator combinations (parlays)
from a batch of pre-analyzed football matches.

------------------------------------------------
GOAL
------------------------------------------------

Build only HIGH QUALITY betting combinations using the
model recommendation already provided in each match.

Only create combinations when the selections are strong enough.

Never force combinations.

------------------------------------------------
STRICT COMBINATION RULES
------------------------------------------------

1. Allowed accumulator size:
   - DOUBLE (2 matches)
   - TREBLE (3 matches)

2. A match Id may appear ONLY ONCE globally across ALL combinations.
   If a match has already been used in a combination, it cannot appear again.

3. Maximum combinations allowed in this batch: 4

4. Selection filter:
   Ignore matches if:
   - Confidence < 65
   - Trap = true
   - Recommendation = "Avoid"

5. Correlation control:
   - Prefer matches from different leagues
   - Maximum 2 matches from same league in a combination

------------------------------------------------
ODDS RULES
------------------------------------------------

Selection odds depend on recommendation:

BTTS → OddsBttsYes  
Over 2.5 Goals → OddsOver25  
Under 2.5 Goals → OddsUnder25  
Match Winner (Home) → OddsHomeWin  
Match Winner (Away) → OddsAwayWin

Accumulator odds = product of all selection odds.

Minimum required accumulator odds = 1.68

If any odds are missing or null:
Ignore the minimum odds rule for that combination.

------------------------------------------------
MATCH USAGE RULE
------------------------------------------------

A match Id may appear ONLY once across ALL combinations.

Example (INVALID):

Combo 1: Match 1001 + Match 1002  
Combo 2: Match 1001 + Match 1003   ❌

Match 1001 already used.

------------------------------------------------
DYNAMIC GENERATION
------------------------------------------------

Do NOT force combinations.

If only 1 valid combination exists → return 1  
If none are good enough → return empty array.

Quality over quantity.

------------------------------------------------
OUTPUT FORMAT (STRICT JSON)
------------------------------------------------

Return ONLY valid JSON.

[
  {
    "combinationId": 1,
    "type": "DOUBLE | TREBLE",
    "totalOdds": number,
    "matches": [
      {
        "fixtureId": number,
        "league": "string",
        "homeTeam": "string",
        "awayTeam": "string",
        "selection": "BTTS | Over 2.5 Goals | Under 2.5 Goals | Match Winner (Home) | Match Winner (Away)",
        "odds": number
      }
    ],
    "reason": "short explanation why this accumulator is strong"
  }
]

Return only JSON.
Do not include explanations outside JSON.
""");

    sb.AppendLine("\nMATCH BATCH DATA (JSON):");

    var json = JsonSerializer.Serialize(
        candidates,
        new JsonSerializerOptions { WriteIndented = true });

    sb.AppendLine(json);

    return sb.ToString();
}


    // ========================= PROMPTS =========================

    /// <summary>System instruction sent to Gemini — professional football match analyst and risk auditor.</summary>
    private static string BuildSystemPrompt() => """
You are a professional football match analyst and betting risk auditor working inside a quantitative prediction system.

You receive MULTIPLE matches grouped by league.
You must analyze EACH match independently.

------------------------------------------------
IMPORTANT GLOBAL RULES
------------------------------------------------

- Process ALL matches in the batch.
- Return ONE result object per fixture.
- Do NOT skip any fixture.
- Be deterministic and consistent.
- Use simple football language (no ML, Poisson, or technical terms).
- Generate TWO fully independent analysis objects:
    1) English version
    2) German version
- Both versions must contain identical meaning.
- Recommendation must remain in English (fixed value from allowed list).

------------------------------------------------
YOUR ROLE
------------------------------------------------

You must:

1. Audit model predictions using match context.
2. Detect statistical traps or misleading situations.
3. Provide final betting recommendation.
4. Explain reasoning clearly.
5. Evaluate consensus model predictions.
6. Assess scoring environment and team strength.

You are BOTH:
- risk auditor
- match analyst

------------------------------------------------
ANALYSIS FLOW (STRICT ORDER)
------------------------------------------------

For EACH match:

STEP 1 — Detect traps
STEP 2 — Evaluate scoring environment
STEP 3 — Evaluate team strength gap
STEP 4 — Validate model probabilities
STEP 5 — Produce final recommendation

Trap detection has highest priority.

------------------------------------------------
TRAP DETECTION RULES (CRITICAL)
------------------------------------------------

Mark trapDetected = true if ANY applies:

- strong vs weak team mismatch
- large class gap causing one-sided result risk
- one team weak attack (<1.2 goals avg)
- high opponent clean sheet rate
- defensive teams
- inflated model probability
- inconsistent form
- lower league volatility
- unrealistic scoring expectation

If trap exists:
Explain exact cause clearly.

------------------------------------------------
SCORING RULES
------------------------------------------------

LOW SCORING signals:
- both teams avg goals < 1.2
- high clean sheet %
- defensive profiles

HIGH SCORING signals:
- strong attacks both sides
- weak defenses
- high recent goals

------------------------------------------------
BTTS VALIDATION
------------------------------------------------

Recommend BTTS ONLY IF:
- both teams realistically score
- reject if one weak attack
- reject vs strong defense

------------------------------------------------
MATCH WINNER RULE
------------------------------------------------

Recommend winner ONLY IF:
- clear quality gap
- reliable form advantage
- stable performance

------------------------------------------------
FINAL RECOMMENDATION (choose ONE)
------------------------------------------------

- "BTTS"
- "Over 2.5 Goals"
- "Under 2.5 Goals"
- "Match Winner (Home)"
- "Match Winner (Away)"
- "Avoid"

------------------------------------------------
OUTPUT REQUIREMENTS FOR EACH MATCH
------------------------------------------------

You MUST return:

1. Final prediction
2. Confidence (0–100)
3. Reason for prediction (2–4 key factors)
4. Deep match analysis (6–8 sentences)
5. Trap detection + trap reason
6. One-line explanation of consensus model predictions
7. One-line summaries for each betting market

------------------------------------------------
RESPONSE FORMAT (STRICT JSON)
------------------------------------------------

Return an ARRAY with one object per fixture:

[
  {
    "fixtureId": number,
    "recommendation": "BTTS | Over 2.5 Goals | Under 2.5 Goals | Match Winner (Home) | Match Winner (Away) | Avoid",
    "confidence": number,
    "trapDetected": true/false,

    "en": {
      "predictionReason": "2-4 key factors",
      "analysis": "6-8 sentence match analysis",
      "trapReason": "exact cause or null",
      "consensusEvaluation": "one sentence explaining model predictions",
      "summaries": {
        "btts": "string",
        "over25": "string",
        "under25": "string",
        "homeWin": "string",
        "awayWin": "string"
      }
    },

    "de": {
      "predictionReason": "2-4 Schlüsselfaktoren",
      "analysis": "6-8 Sätze Spielanalyse",
      "trapReason": "genauer Grund oder null",
      "consensusEvaluation": "ein Satz zur Bewertung der Modellprognosen",
      "summaries": {
        "btts": "string",
        "over25": "string",
        "under25": "string",
        "homeWin": "string",
        "awayWin": "string"
      }
    }
  }
]

Return ONLY valid JSON.
Do NOT include explanations outside JSON.
""";

    /// <summary>User message with match data batch — formats each match for Gemini analysis.</summary>
    private static string BuildMatchPrompt(List<GeminiBatchItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\nMATCH BATCH DATA (JSON):");
        
        // Serialize items with Indented for readability in the prompt
        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
        sb.AppendLine(json);

        return sb.ToString();
    }
           
    /// <summary>Defines the expected JSON response schema for Gemini structured output.</summary>
    private static object GetResponseSchema() 
    {
        var languageBlockSchema = new
        {
            type = "OBJECT",
            properties = new
            {
                predictionReason    = new { type = "STRING" },
                analysis            = new { type = "STRING" },
                trapReason          = new { type = "STRING" },
                consensusEvaluation = new { type = "STRING" },
                summaries = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        btts    = new { type = "STRING" },
                        over25  = new { type = "STRING" },
                        under25 = new { type = "STRING" },
                        homeWin = new { type = "STRING" },
                        awayWin = new { type = "STRING" }
                    }
                }
            },
            required = new[] { "predictionReason", "analysis", "consensusEvaluation", "summaries" }
        };

        return new
        {
            type = "ARRAY",
            items = new
            {
                type = "OBJECT",
                properties = new
                {
                    fixtureId           = new { type = "INTEGER" },
                    recommendation      = new { type = "STRING",
                        @enum = new[] { "BTTS", "Over 2.5 Goals", "Under 2.5 Goals",
                            "Match Winner (Home)", "Match Winner (Away)", "Avoid" } },
                    confidence          = new { type = "NUMBER" },
                    trapDetected        = new { type = "BOOLEAN" },
                    en = languageBlockSchema,
                    de = languageBlockSchema
                },
                required = new[]
                {
                    "fixtureId", "recommendation", "confidence", "trapDetected", "en", "de"
                }
            }
        };
    }

    // ========================= HELPERS =========================

    private static string GetModelFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "gemini-1.5-flash-latest";
        
        // Extract model name between /models/ and :generateContent
        var startIndex = url.IndexOf("/models/");
        if (startIndex == -1) return "gemini-1.5-flash-latest";
        
        startIndex += "/models/".Length;
        var endIndex = url.IndexOf(":", startIndex);
        if (endIndex == -1) endIndex = url.Length;
        
        return url.Substring(startIndex, endIndex - startIndex);
    }

    private bool HasApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            return true;

        _logger.LogWarning("Gemini API key missing");
        return false;
    }





    private class GeminiCombinationResponse
    {
        [JsonPropertyName("combinationId")]
        public int CombinationId { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("totalOdds")]
        public double TotalOdds { get; set; }

        [JsonPropertyName("matches")]
        public List<GeminiCombinationMatch> Matches { get; set; } = [];

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = "";
    }

    private class GeminiCombinationMatch
    {
        [JsonPropertyName("fixtureId")]
        public int FixtureId { get; set; }

        [JsonPropertyName("league")]
        public string League { get; set; } = "";

        [JsonPropertyName("homeTeam")]
        public string HomeTeam { get; set; } = "";

        [JsonPropertyName("awayTeam")]
        public string AwayTeam { get; set; } = "";

        [JsonPropertyName("selection")]
        public string Selection { get; set; } = "";

        [JsonPropertyName("odds")]
        public double Odds { get; set; }
    }

    private static object GetCombinationsSchema() => new
    {
        type = "ARRAY",
        description = "List of generated betting accumulator combinations.",
        items = new
        {
            type = "OBJECT",
            properties = new Dictionary<string, object>
            {
                { "combinationId", new { type = "INTEGER", description = "Sequential ID starting from 1." } },
                { "type", new { type = "STRING", description = "DOUBLE or TREBLE", @enum = new[] { "DOUBLE", "TREBLE" } } },
                { "totalOdds", new { type = "NUMBER", description = "Product of all selection odds." } },
                { "matches", new {
                    type = "ARRAY",
                    description = "The 2 or 3 match selections in this combination.",
                    items = new {
                        type = "OBJECT",
                        properties = new Dictionary<string, object>
                        {
                            { "fixtureId", new { type = "INTEGER" } },
                            { "league", new { type = "STRING" } },
                            { "homeTeam", new { type = "STRING" } },
                            { "awayTeam", new { type = "STRING" } },
                            { "selection", new { type = "STRING", @enum = new[] { "BTTS", "Over 2.5 Goals", "Under 2.5 Goals", "Match Winner (Home)", "Match Winner (Away)" } } },
                            { "odds", new { type = "NUMBER" } }
                        },
                        required = new[] { "fixtureId", "league", "homeTeam", "awayTeam", "selection", "odds" }
                    }
                }},
                { "reason", new { type = "STRING", description = "Short explanation why this accumulator is strong." } }
            },
            required = new[] { "combinationId", "type", "totalOdds", "matches", "reason" }
        }
    };
}