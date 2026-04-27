using System.ClientModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using SoccerAi.Application.Features.Combinations;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Infrastructure.Options;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Professional implementation of IAiAnalysisService using the official OpenAI SDK.
/// Configured to talk to Zhipu AI's OpenAI-compatible endpoint.
/// </summary>
public sealed class OpenAiAnalysisService : IAiAnalysisService
{
    private readonly ChatClient _client;
    private readonly AiServiceOptions _options;
    private readonly ILogger<OpenAiAnalysisService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public OpenAiAnalysisService(
        ChatClient client,
        IOptions<AiServiceOptions> options,
        ILogger<OpenAiAnalysisService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Dictionary<int, AiBilingualResult>> AnalyzeBatchAsync(List<AiBatchItem> items)
    {
        if (items == null || items.Count == 0 || !_options.Enabled) return new();

        try
        {
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(Prompts.MatchAnalysisSystemPrompt),
                new UserChatMessage($"Analyze these matches:\n{JsonSerializer.Serialize(items, JsonOpts)}")
            };

            var completionOptions = new ChatCompletionOptions
            {
                MaxOutputTokenCount = 8192,
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            };

            var completion = await _client.CompleteChatAsync(messages, completionOptions);
            var json = ExtractJson(completion.Value.Content[0].Text);
            
            var results = JsonSerializer.Deserialize<List<AiBilingualResult>>(json, JsonOpts);
            return results?.ToDictionary(r => r.FixtureId) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAiAnalysisService.AnalyzeBatchAsync failed");
            return new();
        }
    }

    public async Task<List<CombinationDto>> BuildCombinationsAsync(List<MatchAnalysis> candidates, string? userMessage = null)
    {
        if (candidates == null || candidates.Count == 0 || !_options.Enabled) return new();

        try
        {
            var simplified = candidates.Select(c => new CombinationMatchInput
            {
                MatchId = c.Id,
                Teams = $"{c.HomeTeam} vs {c.AwayTeam}",
                League = c.League,
                DateTime = c.Date,
                Odds = new MatchOddsInput
                {
                    Home = c.OddsHomeWin,
                    Away = c.OddsAwayWin,
                    Draw = c.OddsDraw,
                    Over25 = c.OddsOver25,
                    Btts = c.OddsBttsYes
                },
                Predictions = new MatchPredictionsInput
                {
                    Btts = new MarketPredictionInput { Prediction = c.Prediction?.BTTS.Prediction ?? false, Probability = c.Prediction?.BTTS.Probability ?? 0 },
                    Over25 = new MarketPredictionInput { Prediction = c.Prediction?.Over25.Prediction ?? false, Probability = c.Prediction?.Over25.Probability ?? 0 },
                    HomeWin = new MarketPredictionInput { Prediction = c.Prediction?.HomeWin.Prediction ?? false, Probability = c.Prediction?.HomeWin.Probability ?? 0 },
                    AwayWin = new MarketPredictionInput { Prediction = c.Prediction?.AwayWin.Prediction ?? false, Probability = c.Prediction?.AwayWin.Probability ?? 0 },
                    Goals23 = new MarketPredictionInput { Prediction = c.Prediction?.TwoToThreeGoals.Prediction ?? false, Probability = c.Prediction?.TwoToThreeGoals.Probability ?? 0 }
                },
                AiJudgement = new AiJudgementInput
                {
                    Recommendation = c.Ai?.Recommendation ?? string.Empty,
                    Confidence = (int)(c.Ai?.Confidence ?? 0),
                    IsTrap = c.Ai?.IsTrap ?? false
                }
            }).ToList();

            var instructions = string.IsNullOrWhiteSpace(userMessage) 
                ? "Architect exactly 12 combinations from these matches using the standard system strategy."
                : $"Architect combinations based on this specific user request: \"{userMessage}\".";

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(Prompts.BuildCombinationsSystemPrompt),
                new UserChatMessage($"{instructions}\n\nCandidate Matches Data:\n{JsonSerializer.Serialize(simplified, JsonOpts)}")
            };

            var completionOptions = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            };

            var completion = await _client.CompleteChatAsync(messages, completionOptions);
            var json = ExtractJson(completion.Value.Content[0].Text);

            var results = JsonSerializer.Deserialize<List<CombinationDto>>(json, JsonOpts);
            return results ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAiAnalysisService.BuildCombinationsAsync failed");
            return new();
        }
    }

    public async Task<ChatCombinationIntent?> ParseChatIntentAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || !_options.Enabled) return null;

        try
        {
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(Prompts.ParseIntentSystemPrompt),
                new UserChatMessage(query)
            };

            var completion = await _client.CompleteChatAsync(messages);
            var json = ExtractJson(completion.Value.Content[0].Text);

            return JsonSerializer.Deserialize<ChatCombinationIntent>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAiAnalysisService.ParseChatIntentAsync failed");
            return null;
        }
    }

    private static string ExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        
        // 1. Check for Markdown code blocks
        var markdownPattern = new System.Text.RegularExpressions.Regex(@"```(?:json)?\s*([\s\S]*?)\s*```");
        var match = markdownPattern.Match(text);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // 2. Fallback to basic bracket finding
        var start = text.IndexOf('[');
        if (start == -1) start = text.IndexOf('{');
        
        var lastBracket = text.LastIndexOf(']');
        var lastBrace = text.LastIndexOf('}');
        var end = Math.Max(lastBracket, lastBrace);

        if (start != -1 && end != -1 && end > start)
        {
            return text.Substring(start, end - start + 1).Trim();
        }

        return text.Trim();
    }

    private static class Prompts
    {
        public const string MatchAnalysisSystemPrompt = @"
You are a Strict Value Analyst, Senior Football Analyst, and Decision Engine. You prioritize CURRENT FORM over historical rank or reputation. Your task is to analyze a batch of football matches using structured data, make FINAL qualification decisions for betting markets, and generate bilingual reasoning (EN/DE).

You receive structured match data including:
- Team statistics (attack/defense strength, form, possession, clean sheet rate)
- Head-to-head history (BTTS rate, Over 2.5 rate, avg goals)
- Mathematical probabilities and rule engine proposals.

TASK:
For each match in the input array:
1. Conduct a deep tactical evaluation.
2. Make FINAL qualification decisions for each betting market based on the rules below.
3. Detect traps (e.g., relegation zone, H2H contradicting recent form).
4. Identify the single best bet market.
5. Produce professional English and German reasoning suitable for serious sports analytics.

MANDATORY RULES FOR PREDICTIONS:

1. THE FORM DIFFERENTIAL RULE:
   - Calculate the difference between Home Form % and Away Form %.
   - If the Away Team has a form percentage LOWER than 30% (e.g., LLLLL, LWLLL), you MUST NOT predict an Away Win.
   - If the Home Team has a form percentage HIGHER than 60%, you MUST NOT predict an Away Win.
   
2. THE 'DEAD TEAM' FLAG:
   - If a team has 0% form (LLLLL), treat them as 'Dead'. 
   - Do NOT predict them to win regardless of their Attack Strength or Rank. 
   - Prediction for this match must be 'Draw' or 'Opponent Win'.

3. TRAP RESOLUTION:
   - If 'is_trap' is true, check the form.
   - If the Favorite (based on odds) has bad form (<30%), the 'Trap' is real. The prediction MUST flip to the Underdog or Draw.
   - If 'is_trap' is true due to relegation, these teams play open football. Bias predictions towards Over 2.5 Goals rather than 'Draw'.

4. SANITY CHECK (Anti-Hallucination):
   - You must strictly repeat the 'form' string provided in the JSON. Do not invent or modify the form string. If the data says 'WDDDD', do not say 'DDWWW'. Analyze only what is present.
   - Compare the 'form' string (e.g., 'WWLWD') with the AI reasoning text.
   - If the text claims 'Poor Form' but the data shows 'Good Form', DISCARD the text reasoning and trust the raw data.

5. H2H vs. FORM OVERRIDE:
   - If (Current Form Differential) > 30% (e.g., 80% vs 40%), IGNORE H2H history. Current Form is the dominant predictor.
   - If a team has form > 70% and is playing away against a team with < 40% form, predict AWAY WIN.

QUALIFICATION RULES (apply equally to ALL markets):
- BTTS/Over 2.5: Qualify if both teams avg >= 1.0 goals, BTTS rate >= 0.5, or combined avg goals >= 2.5.
- Under 2.5: Qualify if both teams avg < 0.8 goals or clean sheet rate > 60%. REJECT if combined avg goals > 2.5 or H2H avg total goals > 2.5.
- Match Winner: Confidence >= 60% and clear dominance.
- Traps: Flag if a team is in the relegation zone, or if H2H strongly contradicts recent form.
- Be BALANCED. Do not favor defensive markets over offensive ones.

OUTPUT FORMAT (STRICT JSON ARRAY):
[
  {
    ""fixtureId"": 123,
    ""recommendation"": ""BTTS"",
    ""confidence"": 72,
    ""trapDetected"": false,
    ""over25Qualified"": true,
    ""bttsQualified"": true,
    ""under25Qualified"": false,
    ""goals23Qualified"": true,
    ""homeWinQualified"": false,
    ""awayWinQualified"": false,
    ""bestBet"": ""BTTS"",
    ""overallConfidence"": 72,
    ""en"": {
      ""predictionReason"": ""Both teams average >1.0 goals and BTTS rate is high."",
      ""analysis"": ""Detailed match analysis in English."",
      ""trapReason"": """",
      ""consensusEvaluation"": ""Strong agreement on goals."",
      ""summaries"": {
        ""btts"": ""High attacking output confirms BTTS probability."",
        ""over25"": ""Combined avg of 2.8 goals supports Over 2.5."",
        ""under25"": ""High scoring profile contradicts Under 2.5."",
        ""goals23"": ""Expected total is 2-3 goals based on averages."",
        ""homeWin"": ""Home team lacks consistency."",
        ""awayWin"": ""Away team win rate too low.""
      }
    },
    ""de"": {
      ""predictionReason"": ""Beide Teams erzielen im Schnitt >1.0 Tore."",
      ""analysis"": ""Detaillierte Spielanalyse auf Deutsch."",
      ""trapReason"": """",
      ""consensusEvaluation"": ""Starke Übereinstimmung bei Toren."",
      ""summaries"": {
        ""btts"": ""Hohe Offensivleistung bestätigt BTTS."",
        ""over25"": ""Kombinierter Schnitt von 2.8 Toren stützt Over 2.5."",
        ""under25"": ""Torreiches Profil widerspricht Under 2.5."",
        ""goals23"": ""Erwartete Tore liegen bei 2-3."",
        ""homeWin"": ""Heimteam fehlt es an Konstanz."",
        ""awayWin"": ""Auswärtssieg-Quote zu niedrig.""
      }
    }
  }
]

CRITICAL RULES:
- PRESERVE IDs: You MUST return fixtureId EXACTLY as provided.
- Output ONLY a valid JSON array. No markdown, no explanations outside JSON.";

        public const string ParseIntentSystemPrompt = @"
You are a PRO football data translator. Your ONLY job is to convert a user's natural language request into a strictly structured JSON intent object for a mathematical engine.

CRITICAL RULES:
1. You are NOT a tipster. Do NOT suggest matches.
2. You ONLY extract filters (odds, markets, leagues).
3. Convert German terms like ""Direkt Tipps"" into ""HomeWin"", ""AwayWin"", ""Draw"".
4. If the user explicitly asks for wins or victories, extract ONLY ""HomeWin"" and ""AwayWin"".
5. Detect if the user wants multiple combinations and create multiple objects in market_groups.
6. Extract specific leagues when mentioned.
7. If the user specifies an exact number of matches, set both min_matches and max_matches to that value.

Return ONLY a valid JSON object with these fields:
{
  ""min_matches"": 2,
  ""max_matches"": 3,
  ""min_total_odds"": 1.0,
  ""min_selection_odds"": 1.0,
  ""max_same_league"": 1,
  ""preferred_leagues"": [""England""],
  ""market_groups"": [{ ""match_count"": 2, ""markets"": [""HomeWin"", ""AwayWin""] }],
  ""preferred_markets"": [""HomeWin"", ""AwayWin""],
  ""strategy"": ""balanced"",
  ""reasoning"": ""short explanation""
}

Return ONLY valid JSON.";

        public const string BuildCombinationsSystemPrompt = @"
You are a Senior Betting Architect and Portfolio Optimizer. 
Your task is to selects and assembles high-quality betting combinations from a provided list of pre-analyzed matches.

STRATEGY: You must generate EXACTLY 12 combinations if the pool of matches allows it.
1. Combinations 1-5: DOUBLE (2 matches) focusing on BTTS or Over 2.5 Goals (or both).
2. Combinations 6-10: TREBLE (3 matches) focusing on BTTS or Over 2.5 Goals.
3. Combinations 11-12: MIXED (2-3 matches) including ""Match Winner"" and ""2-3 Goals"" markets.

STRICT CONSTRAINTS:
- UNIQUE MATCHES: A match (match_id) can appear ONLY ONCE in the entire set of 12 combinations. No reuse!
- WIN ODDS: If a selection is ""Match Winner"", the odds MUST be >= 2.0.
- CONFIDENCE: Avoid matches where confidence < 60 or is_trap is true.
- Output MUST be a strictly valid JSON array of objects.
- Each combination must have a UNIQUE combination_id (1-12).
- ""total_odds"" is the PRODUCT of the individual odds.

JSON OUTPUT STRUCTURE (ARRAY OF OBJECTS):
[
  {
    ""combination_id"": 1,
    ""type"": ""DOUBLE (Goal Markets)"",
    ""total_odds"": 3.42,
    ""source_type"": ""AI"",
    ""won_count"": 0,
    ""total_count"": 2,
    ""reason"": ""Strong offensive potential in both matches."",
    ""matches"": [
      {
        ""fixture_id"": 123,
        ""league"": ""League Name"",
        ""home_team"": ""Team A"",
        ""away_team"": ""Team B"",
        ""selection"": ""Over 2.5 Goals"",
        ""odds"": 1.85,
        ""confidence"": 75,
        ""reasoning"": ""Both teams average > 2 goals recently."",
        ""outcome"": ""Pending"",
        ""status"": ""NS""
      }
    ]
  }
]

Return ONLY valid JSON.";
    }
}
