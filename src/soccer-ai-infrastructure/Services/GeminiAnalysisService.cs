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

    public async Task<List<CombinationDto>> BuildCombinationsAsync(List<CombinationMatchDto> candidates)
    {
        if (candidates == null || candidates.Count == 0 || !HasApiKey())
            return new();

        var parsed = await ExecuteGeminiRequest<List<GeminiCombinationResponse>>(
            BuildCombinationsPrompt(candidates), null); // Combinations don't have a defined schema yet or use default

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
            try
            {
                Schema? geminiSchema = null;
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

                var modelName = GetModelFromUrl(_options.BaseUrl);
                
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
        List<CombinationMatchDto> candidates)
    {
        var usedFixtures = new HashSet<int>();
        var result = new List<CombinationDto>();

        foreach (var combo in raw)
        {
            if (combo.FixtureIds.Count is < 2 or > 3)
                continue;

            if (combo.FixtureIds.Any(id => usedFixtures.Contains(id)))
                continue;

            var matches = candidates
                .Where(c => combo.FixtureIds.Contains(c.FixtureId))
                .ToList();

            if (matches.Count != combo.FixtureIds.Count)
                continue;

            foreach (var id in combo.FixtureIds)
                usedFixtures.Add(id);

            result.Add(new CombinationDto(combo.Name, matches));
        }

        return result;
    }

    // ========================= PROMPTS =========================

    private static string BuildCombinationsPrompt(List<CombinationMatchDto> candidates)
    {
        var sb = new StringBuilder();

        sb.AppendLine("""
You are a professional betting portfolio optimizer.

GOAL:
Create EXACTLY 9 unique betting combinations.

STRICT RULES (MUST FOLLOW):
1. Create:
   - 4 DOUBLE combinations (2 matches each)
   - 5 TREBLE combinations (3 matches each)

2. A fixture can appear ONLY ONCE across ALL combinations.
   NEVER reuse a match.

3. Prioritize:
   - highest confidence
   - highest expected value
   - low risk correlation

4. Prefer different leagues inside same combo. if not possible then use max two matches from sane league

5. If uniqueness cannot be satisfied → return fewer combinations.

Return only JSON.
""");

        sb.AppendLine("\nCANDIDATES:");

        foreach (var c in candidates.OrderByDescending(x => x.Confidence))
        {
            sb.AppendLine(
                $"ID:{c.FixtureId} | {c.HomeTeam} vs {c.AwayTeam} | {c.LeagueName} | {c.Market}:{c.Prediction} | Odds:{c.Odds:F2} | Conf:{c.Confidence}% | EV:{c.ExpectedValue:F2}");
        }

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
        public string Name { get; set; } = "";
        public List<int> FixtureIds { get; set; } = new();
    }
}