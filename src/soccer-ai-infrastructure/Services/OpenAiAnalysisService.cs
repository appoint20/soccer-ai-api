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
                MaxOutputTokenCount = 4096,
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
You are a football data analyst, not a chatbot. Your task is to analyze a batch of football matches using structured data and return predictions with clear reasoning.

IMPORTANT RULES:
- Be analytical and data-driven.
- Do NOT guess randomly.
- Do NOT use vague language.
- Base your decision ONLY on the provided data.
- If confidence is low, say so.
- REQUIRED: You must process EVERY match in the provided batch.
- CONCISE: Keep bullets and summaries short (max 1-2 sentences) to avoid token limits.

TASK:
For each match in the input array:
1. Conduct a deep tactical evaluation using the provided metrics: league rank, points, recent form, possession style, attacking/defensive strength, ELO ratings, and historical averages (Last 3/7 games).
2. Compare the model-generated probabilities (Poisson/MC) against the market odds to identify Value Bets or potential traps.
3. Calculate an internal confidence score (0-100).
4. Predict ONE outcome from this list: ""Btts"", ""Over 2.5 Goals"", ""Under 2.5 Goals"", ""2-3 Goals"", ""BTTS and Over 2.5"", ""Match Winner (Home)"", ""Draw"", ""Match Winner (Away)"".
5. Detect if it is a risk trap based on momentum vs. rank discrepancies.
6. Produce professional English and German reasoning suitable for serious sports analytics.

CONSTRAINTS:
- No hallucinated data or external knowledge.
- PRESERVE IDs: You MUST return fixtureId EXACTLY as provided.
- Output must be a STRICT JSON array of objects.
- Generate bilingual results (EN and DE).

OUTPUT FORMAT (STRICT JSON ARRAY):
[
  {
    ""fixtureId"": 123,
    ""recommendation"": ""Match Winner (Home)"",
    ""confidence"": 72,
    ""trapDetected"": false,
    ""en"": {
      ""predictionReason"": ""Short bullet-style reasoning."",
      ""analysis"": ""Detailed analysis in English."",
      ""trapReason"": ""Optional trap reason."",
      ""consensusEvaluation"": ""Agreement between signals."",
      ""summaries"": {
        ""btts"": ""Short BTTS summary."",
        ""over25"": ""Short over 2.5 summary."",
        ""under25"": ""Short under 2.5 summary."",
        ""homeWin"": ""Short home win summary."",
        ""awayWin"": ""Short away win summary.""
      }
    },
    ""de"": {
      ""predictionReason"": ""Kurze Begruendung auf Deutsch."",
      ""analysis"": ""Detaillierte Analyse auf Deutsch."",
      ""trapReason"": ""Optionale Trap-Erklaerung."",
      ""consensusEvaluation"": ""Bewertung der Signale."",
      ""summaries"": {
        ""btts"": ""Kurze BTTS-Zusammenfassung."",
        ""over25"": ""Kurze Over-2.5-Zusammenfassung."",
        ""under25"": ""Kurze Under-2.5-Zusammenfassung."",
        ""homeWin"": ""Kurze Heimsieg-Zusammenfassung."",
        ""awayWin"": ""Kurze Auswaertssieg-Zusammenfassung.""
      }
    }
  }
]

Return ONLY valid JSON.";

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
