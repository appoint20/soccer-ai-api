using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

public class GeminiService : IGeminiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model = "gemini-2.0-flash-exp";
    private readonly ILogger<GeminiService> _logger;

    public GeminiService(HttpClient httpClient, IConfiguration config, ILogger<GeminiService> logger)
    {
        _httpClient = httpClient;
        _apiKey = config["GoogleGemini:ApiKey"] ?? throw new ArgumentNullException("GoogleGemini:ApiKey not configured");
        _logger = logger;
    }

    public async Task<AnalyzedMatchDto> AnalyzeMatchAsync(AnalyzedMatchDto input)
    {
        var prompt = ConstructAnalysisPrompt(input);
        var response = await CallGeminiJsonAsync(prompt);
        
        // Parse "prediction", "reasoning", "confidence" from JSON response
        try 
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            input.AiPrediction = root.GetProperty("prediction").GetString() ?? "Skip";
            input.AiReasoning = root.GetProperty("reasoning").GetString() ?? "";
            input.AiConfidence = root.GetProperty("confidence").GetDouble();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Gemini Analysis response");
            input.AiPrediction = "Error";
            input.AiReasoning = "AI Analysis Failed";
        }

        return input;
    }

    public async Task<List<GeminiTicketResponse>> GenerateTicketsAsync(List<AnalyzedMatchDto> candidates)
    {
        var prompt = ConstructTicketPrompt(candidates);
        var jsonResponse = await CallGeminiJsonAsync(prompt);
        
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var tickets = JsonSerializer.Deserialize<List<GeminiTicketResponse>>(jsonResponse, options);
            return tickets ?? new List<GeminiTicketResponse>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Gemini Ticket response");
            return new List<GeminiTicketResponse>();
        }
    }

    private async Task<string> CallGeminiJsonAsync(string prompt)
    {
        var requestBody = new
        {
            contents = new[] {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new {
                responseMimeType = "application/json"
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

        var response = await _httpClient.PostAsync(url, content);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Gemini API Failed: {StatusCode} - {Content}. Request: {Request}", response.StatusCode, errorContent, json);
            response.EnsureSuccessStatusCode();
        }

        var responseString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseString);
        
        // Extract text from Gemini response structure
        // candidates[0].content.parts[0].text
        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        return text ?? "{}";
    }

    private string ConstructAnalysisPrompt(AnalyzedMatchDto match)
    {
        // Serialize relevant data for prompt context
        var data = JsonSerializer.Serialize(new {
            Match = $"{match.HomeTeam} vs {match.AwayTeam}",
            Stats = new { Home = match.HomeStats.FormLast5, Away = match.AwayStats.FormLast5, H_AvG = match.HomeStats.AvgGoalsFor, A_AvG = match.AwayStats.AvgGoalsFor },
            Math = match.MathProbabilities,
            MonteCarlo = match.MonteCarlo,
            ML = match.MlPrediction
        });

        return $@"
Role: Expert Sports Bettor & Data Analyst.
Task: Analyze this match data strictly.
Inputs: {data}

Instructions:
1. Compare ML Model Confidence vs Math Probabilities vs Team Stats.
2. Look for contradictions (e.g. ML says Over 2.5 but Stats say 0-0 form).
3. If signals align, select the Best Prediction.
4. If signals conflict, verify if 'Both Teams To Score' or 'Home Win' is safer.
5. Provide a short reasoning string.
6. Confidence 0.0 to 1.0.

Output JSON:
{{
  ""prediction"": ""Over 2.5 Goals"" OR ""BTTS"" OR ""Home Win"",
  ""reasoning"": ""..."",
  ""confidence"": 0.85
}}";
    }

    private string ConstructTicketPrompt(List<AnalyzedMatchDto> candidates)
    {
        // Minimal data for token efficiency
        var pool = candidates.Select(c => new {
            Id = c.MatchId,
            Match = $"{c.HomeTeam} vs {c.AwayTeam}",
            Pred = c.AiPrediction,
            Conf = c.AiConfidence,
            Reason = c.AiReasoning,
            Odds = c.Odds
        }).ToList();
        
        var poolJson = JsonSerializer.Serialize(pool);

        return $@"
Role: Ticket Architect.
Task: Create exactly 7 Betting Tickets from the provided pool of high-quality predictions.
Pool: {poolJson}

Constraints:
1. Create EXACTLY 7 tickets.
2. Each ticket MUST have at least 3 games.
3. Total Odds per ticket MUST be > 1.77.
4. Diversify! Do not just repeat the same 3 matches. Mix and match.
5. Use the provided 'Pred' (Prediction) as the selection.
6. Calculate 'total_odds' correctly.

Output JSON Array:
[
  {{
    ""ticket_id"": 1,
    ""matches"": [
      {{ ""match"": ""Team A vs Team B"", ""selection"": ""Over 2.5"", ""odds"": 1.50 }}
    ],
    ""total_odds"": 3.37,
    ""analysis"": ""High confidence accumulator...""
  }}
]";
    }
}
