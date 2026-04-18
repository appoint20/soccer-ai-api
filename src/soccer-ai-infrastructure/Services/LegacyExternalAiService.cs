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
using DataType = Google.GenAI.Types.Type;

namespace SoccerAi.Infrastructure.Services;

public sealed class LegacyExternalAiService : IAiAnalysisService
{
    private const int BatchSize = 10;

    private readonly LegacyAiOptions _options;
    private readonly ILogger<LegacyExternalAiService> _logger;
    private readonly Client _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public LegacyExternalAiService(
        IOptions<LegacyAiOptions> options,
        ILogger<LegacyExternalAiService> logger)
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

    public async Task<Dictionary<int, AiBilingualResult>> AnalyzeBatchAsync(List<AiBatchItem> items)
    {
        if (items == null || items.Count == 0 || !HasApiKey())
            return new();

        var result = new Dictionary<int, AiBilingualResult>();
        
        // Group by league for better context
        var leagues = items.GroupBy(x => x.League);

        foreach (var leagueGroup in leagues)
        {
            var leagueItems = leagueGroup.ToList();
            
            foreach (var chunk in leagueItems.Chunk(BatchSize))
            {
                var payload = BuildSystemPrompt() + "\n\n" + BuildMatchPrompt(chunk.ToList());
                var parsed = await ExecuteLegacyRequest<List<AiBilingualResult>>(payload, GetResponseSchema());

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

        var parsed = await ExecuteLegacyRequest<List<LegacyAiCombinationResponse>>(
            BuildCombinationsPrompt(candidates), GetCombinationsSchema());

        if (parsed == null)
            return new();

        var validated = ValidateAndBuildCombinations(parsed, candidates);

        return validated;
    }

    // ========================= GEMINI CALL =========================

    private async Task<T?> ExecuteLegacyRequest<T>(string prompt, object? schema = null)
    {
        const int maxRetries = 3;
        var delayMs = 2000;

        for (var i = 0; i < maxRetries; i++)
        {
            var geminiSchema = schema as Schema;
            try
            {

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
                    modelName = "gemini-2.5-flash"; // Ultimate fallback
                
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
                    _logger.LogError(ex, "Failed to deserialize Legacy AI response: {Text}", text);
                    return default;
                }
            }
            catch (Exception ex)
            {
                // DETECT QUOTA EXCEEDED (HTTP 429)
                if (ex.Message.Contains("quota") || ex.Message.Contains("429") || (ex.InnerException?.Message.Contains("429") ?? false))
                {
                    var currentModel = _options.Model ?? GetModelFromUrl(_options.BaseUrl);
                    
                    if (currentModel != "gemini-2.5-flash")
                    {
                        _logger.LogWarning("Gemini Primary Model ({Model}) Quota Exceeded. Falling back to gemini-2.5-flash for resilience...", currentModel ?? "Unknown");
                        
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
                                "gemini-2.5-flash", 
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
                            _logger.LogCritical(fallbackEx, "Legacy AI FALLBACK Model (Flash) also failed or hit quota.");
                            throw new AiQuotaExceededException("Both primary and fallback AI models reached quota.", fallbackEx);
                        }
                    }

                    _logger.LogCritical("AI Quota Exceeded! Specific error: {Msg}", ex.Message);
                    throw new AiQuotaExceededException("AI daily quota reached. Please wait for reset or upgrade plan.", ex);
                }

                _logger.LogError(ex, "Legacy AI request failed (Attempt {Attempt})", i + 1);
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

    public async Task<ChatCombinationIntent?> ParseChatIntentAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        if (!HasApiKey())
        {
            _logger.LogWarning("[LegacyAi] API Key missing. Falling back to rule-based parser.");
            return FallbackParseIntent(query);
        }

        try
        {
            var prompt = BuildChatIntentPrompt(query);
            var result = await ExecuteLegacyRequest<ChatCombinationIntent>(prompt, GetChatIntentSchema());

            if (result != null)
            {
                _logger.LogInformation("[LegacyAi] Successfully parsed intent: {Intent}", JsonSerializer.Serialize(result));
                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LegacyAi] Chat parsing failed. Using fallback.");
        }

        return FallbackParseIntent(query);
    }

    private ChatCombinationIntent FallbackParseIntent(string query)
    {
        var q = query.ToLower();
        var intent = new ChatCombinationIntent();

        // Count detection
        if (q.Contains("three") || q.Contains("3")) { intent.MinMatches = 3; intent.MaxMatches = 3; }
        else if (q.Contains("two") || q.Contains("2")) { intent.MinMatches = 2; intent.MaxMatches = 2; }

        // Market detection
        if (q.Contains("win") || q.Contains("victory")) intent.PreferredMarkets.Add("HomeWin");
        if (q.Contains("btts") || q.Contains("both team")) intent.PreferredMarkets.Add("BTTS");
        if (q.Contains("over") || q.Contains("2.5")) intent.PreferredMarkets.Add("Over25");

        // Odds detection (simple regex)
        var match = System.Text.RegularExpressions.Regex.Match(q, @"\d+\.?\d*");
        if (match.Success && double.TryParse(match.Value, out var odds) && odds > 1.0)
        {
            intent.MinTotalOdds = odds;
        }

        intent.Reasoning = "Parsed using rule-based fallback logic.";
        return intent;
    }

    private string BuildChatIntentPrompt(string query) => $"""
        You are a PRO football Data Translator. Your ONLY job is to convert a user's natural language request into a strictly structured JSON intent object for a mathematical engine.
        
        CRITICAL RULES:
        1. You are NOT a tipster. Do NOT suggest matches.
        2. You ONLY extract filters (odds, markets, leagues).
        3. Convert German terms like "Direkt Tipps" into "HomeWin", "AwayWin", "Draw".
        4. If the user explicitly asks for 'wins' or 'victories', extract ONLY 'HomeWin' and 'AwayWin'. Do NOT extract 'Draw'.
        5. Detect if the user wants multiple combinations (e.g., "1 Treble and 1 Double") and create multiple objects in the `market_groups` list.
        6. Extract any specific leagues mentioned by the user (e.g., "English leagues" -> ["England"], "Champions League" -> ["Champions League"]).
        7. If the user explicitly specifies the number of matches (e.g. "three match combine"), you MUST set both `min_matches` and `max_matches` to that exact number (e.g., 3).
        8. If the user specifies a time window (e.g., "between 11:00-15:00"), strictly populate `time_frame` with `start_time` and `end_time` (format: HH:mm:ss).

        USER QUERY: "{query}"

        Return ONLY a valid JSON object matching the requested schema.
        """;

    private Schema GetChatIntentSchema() 
    {
        var groupSchema = new Schema
        {
            Type = DataType.Object,
            Properties = new Dictionary<string, Schema>
            {
                ["match_count"] = new Schema { Type = DataType.Integer },
                ["markets"] = new Schema
                {
                    Type = DataType.Array,
                    Items = new Schema { 
                        Type = DataType.String,
                        Enum = new List<string> { "HomeWin", "AwayWin", "Draw", "BTTS", "Over25", "Under25" }
                    }
                }
            },
            Required = new List<string> { "match_count", "markets" }
        };

        var timeFrameSchema = new Schema
        {
            Type = DataType.Object,
            Properties = new Dictionary<string, Schema>
            {
                ["start_time"] = new Schema { Type = DataType.String, Description = "e.g., 11:00:00" },
                ["end_time"] = new Schema { Type = DataType.String, Description = "e.g., 15:00:00" }
            }
        };

        return new Schema
        {
            Type = DataType.Object,
            Properties = new Dictionary<string, Schema>
            {
                ["min_matches"] = new Schema { Type = DataType.Integer }, 
                ["max_matches"] = new Schema { Type = DataType.Integer }, 
                ["time_frame"] = timeFrameSchema,
                ["min_total_odds"] = new Schema { Type = DataType.Number },
                ["min_selection_odds"] = new Schema { Type = DataType.Number },
                ["max_same_league"] = new Schema { Type = DataType.Integer },
                ["preferred_leagues"] = new Schema { Type = DataType.Array, Items = new Schema { Type = DataType.String } },
                ["market_groups"] = new Schema
                {
                    Type = DataType.Array,
                    Items = groupSchema
                },
                ["preferred_markets"] = new Schema { Type = DataType.Array, Items = new Schema { Type = DataType.String } },
                ["strategy"] = new Schema { Type = DataType.String, Enum = new List<string> { "safe", "balanced", "aggressive" } },
                ["reasoning"] = new Schema { Type = DataType.String }
            },
            Required = new List<string> { "min_total_odds", "min_selection_odds", "max_same_league", "market_groups", "strategy", "reasoning" }
        };
    }

    // ========================= VALIDATION =========================

    private List<CombinationDto> ValidateAndBuildCombinations(
        List<LegacyAiCombinationResponse> raw,
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
                Matches = combo.Matches.Select(m => 
                {
                    var source = candidates.FirstOrDefault(c => c.Id == m.FixtureId);
                    return new CombinationMatchDto
                    {
                        FixtureId = m.FixtureId,
                        League = source?.League ?? m.League,
                        HomeTeam = source?.HomeTeam ?? m.HomeTeam,
                        AwayTeam = source?.AwayTeam ?? m.AwayTeam,
                        Selection = m.Selection,
                        Odds = m.Odds,
                        Confidence = source?.Ai?.Confidence ?? 0.0,
                        Reasoning = source?.Ai?.Reasoning ?? string.Empty
                    };
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

5. Correlation control & Diversity:
   - Prefer matches from different leagues
   - Maximum 2 matches from same league in a combination
   - **MANDATORY**: Favor combinations that MIX different markets (e.g., combining a 'Match Winner' selection with a 'BTTS' or 'Over 2.5' selection). 
   - Avoid creating combinations where all matches have the same selection type (e.g., Avoid 3x BTTS) unless those are the ONLY high-confidence options available.

------------------------------------------------
DIVERSITY & MIXED MARKETS
------------------------------------------------

A balanced accumulator (e.g., Home Win + BTTS) is often more reliable than three identical market picks. 
Your primary goal is to provide a "Mixed Bag" of high-confidence picks. 

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
Minimum required odds for wins predictions = 2.0

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

------------------------------------------------
MARKET PRIORITIZATION (STRICT)
------------------------------------------------

- PRIMARY: "BTTS", "Over 2.5 Goals", "Under 2.5 Goals". Prefer these if they are safe.
- SECONDARY: "Match Winner (Home)", "Match Winner (Away)". Use only if primary markets are low-confidence but a win is very clear.
- FORBIDDEN: NEVER recommend "Draw". If a draw is likely, recommend "Avoid" or "Under 2.5 Goals" instead.

------------------------------------------------
ANALYSIS FLOW (STRICT ORDER)
------------------------------------------------

For EACH match:

STEP 1 — Detect traps
STEP 2 — Evaluate scoring environment
STEP 3 — Evaluate team strength gap
STEP 4 — Validate model probabilities
STEP 5 — Produce final recommendation (Favoring PRIMARY markets)

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

    /// <summary>User message with match data batch — formats each match for AI analysis.</summary>
    private static string BuildMatchPrompt(List<AiBatchItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\nMATCH BATCH DATA (JSON):");
        
        // Serialize items with Indented for readability in the prompt
        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
        sb.AppendLine(json);

        return sb.ToString();
    }
           
    /// <summary>Defines the expected JSON response schema for Gemini structured output.</summary>
    private static Schema GetResponseSchema() 
    {
        var languageBlockSchema = new Schema
        {
            Type = DataType.Object,
            Properties = new Dictionary<string, Schema>
            {
                ["predictionReason"] = new Schema { Type = DataType.String },
                ["analysis"] = new Schema { Type = DataType.String },
                ["trapReason"] = new Schema { Type = DataType.String },
                ["consensusEvaluation"] = new Schema { Type = DataType.String },
                ["summaries"] = new Schema
                {
                    Type = DataType.Object,
                    Properties = new Dictionary<string, Schema>
                    {
                        ["btts"] = new Schema { Type = DataType.String },
                        ["over25"] = new Schema { Type = DataType.String },
                        ["under25"] = new Schema { Type = DataType.String },
                        ["homeWin"] = new Schema { Type = DataType.String },
                        ["awayWin"] = new Schema { Type = DataType.String }
                    }
                }
            },
            Required = new List<string> { "predictionReason", "analysis", "consensusEvaluation", "summaries" }
        };

        return new Schema
        {
            Type = DataType.Array,
            Items = new Schema
            {
                Type = DataType.Object,
                Properties = new Dictionary<string, Schema>
                {
                    ["fixtureId"] = new Schema { Type = DataType.Integer },
                    ["recommendation"] = new Schema 
                    { 
                        Type = DataType.String,
                        Enum = new List<string> { "BTTS", "Over 2.5 Goals", "Under 2.5 Goals", "Match Winner (Home)", "Match Winner (Away)", "Avoid" }
                    },
                    ["confidence"] = new Schema { Type = DataType.Number },
                    ["trapDetected"] = new Schema { Type = DataType.Boolean },
                    ["en"] = languageBlockSchema,
                    ["de"] = languageBlockSchema
                },
                Required = new List<string> { "fixtureId", "recommendation", "confidence", "trapDetected", "en", "de" }
            }
        };
    }

    // ========================= HELPERS =========================

    private static string GetModelFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "gemini-2.5-flash";
        
        // Extract model name between /models/ and :generateContent
        var startIndex = url.IndexOf("/models/");
        if (startIndex == -1) return "gemini-2.5-flash";
        
        startIndex += "/models/".Length;
        var endIndex = url.IndexOf(":", startIndex);
        if (endIndex == -1) endIndex = url.Length;
        
        return url.Substring(startIndex, endIndex - startIndex);
    }

    private bool HasApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            return true;

        _logger.LogWarning("Legacy AI API key missing");
        return false;
    }





    private class LegacyAiCombinationResponse
    {
        [JsonPropertyName("combinationId")]
        public int CombinationId { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("totalOdds")]
        public double TotalOdds { get; set; }

        [JsonPropertyName("matches")]
        public List<LegacyAiCombinationMatch> Matches { get; set; } = [];

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = "";
    }

    private class LegacyAiCombinationMatch
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

    private static Schema GetCombinationsSchema() => new Schema
    {
        Type = DataType.Array,
        Description = "List of generated betting accumulator combinations.",
        Items = new Schema
        {
            Type = DataType.Object,
            Properties = new Dictionary<string, Schema>
            {
                ["combinationId"] = new Schema { Type = DataType.Integer, Description = "Sequential ID starting from 1." },
                ["type"] = new Schema { Type = DataType.String, Description = "DOUBLE or TREBLE", Enum = new List<string> { "DOUBLE", "TREBLE" } },
                ["totalOdds"] = new Schema { Type = DataType.Number, Description = "Product of all selection odds." },
                ["matches"] = new Schema {
                    Type = DataType.Array,
                    Description = "The 2 or 3 match selections in this combination.",
                    Items = new Schema {
                        Type = DataType.Object,
                        Properties = new Dictionary<string, Schema>
                        {
                            ["fixtureId"] = new Schema { Type = DataType.Integer },
                            ["league"] = new Schema { Type = DataType.String },
                            ["homeTeam"] = new Schema { Type = DataType.String },
                            ["awayTeam"] = new Schema { Type = DataType.String },
                            ["selection"] = new Schema { Type = DataType.String, Enum = new List<string> { "BTTS", "Over 2.5 Goals", "Under 2.5 Goals", "Match Winner (Home)", "Match Winner (Away)" } },
                            ["odds"] = new Schema { Type = DataType.Number }
                        },
                        Required = new List<string> { "fixtureId", "league", "homeTeam", "awayTeam", "selection", "odds" }
                    }
                },
                ["reason"] = new Schema { Type = DataType.String, Description = "Short explanation why this accumulator is strong." }
            },
            Required = new List<string> { "combinationId", "type", "totalOdds", "matches", "reason" }
        }
    };
}