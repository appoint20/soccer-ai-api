using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_infrastructure.Services;

public class GeminiAnalysisService(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<GeminiAnalysisService> logger)
    : IGeminiAnalysisService
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient();
    private readonly string _apiKey = config["GoogleGemini:ApiKey"] ?? throw new ArgumentNullException("GoogleGemini:ApiKey");
    private readonly string _model = "gemini-2.0-flash-exp";

    public async Task<Dictionary<string, GeminiMatchAnalysis>> AnalyzeMatchBatchAsync(
        string leagueName,
        List<UpcomingMatchDto> matches,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!matches.Any())
                return new Dictionary<string, GeminiMatchAnalysis>();

            logger.LogInformation("Analyzing batch of {Count} matches for {League}", matches.Count, leagueName);

            // Send COMPLETE match data to Gemini - let it decide what's important
            var batchResponse = await AnalyzeMatchesBatchAsync(leagueName, matches, cancellationToken);

            // Map responses back to dictionary
            var results = new Dictionary<string, GeminiMatchAnalysis>();
            
            foreach (var analysis in batchResponse.Analyses)
            {
                var match = matches.FirstOrDefault(m => 
                    $"{m.HomeTeam}-{m.AwayTeam}-{m.Date}" == analysis.MatchId);
                
                if (match == null) continue;

                var key = $"{match.HomeTeam}-{match.AwayTeam}";
                results[key] = new GeminiMatchAnalysis
                {
                    MatchKey = key,
                    HomeTeam = match.HomeTeam,
                    AwayTeam = match.AwayTeam,
                    League = leagueName,
                    Date = match.Date,
                    Analysis = analysis.Analysis,
                    Prediction = analysis.Prediction,
                    ConfidenceLevel = analysis.Confidence,
                    Reason = analysis.Reasoning,
                    GeneratedAt = DateTime.UtcNow
                };
            }

            logger.LogInformation("Completed batch analysis for {League}: {Count} analyses", leagueName, results.Count);
            return results;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in batch analysis for {League}", leagueName);
            return new Dictionary<string, GeminiMatchAnalysis>();
        }
    }

    private async Task<GeminiBatchAnalysisResponse> AnalyzeMatchesBatchAsync(
        string leagueName,
        List<UpcomingMatchDto> matches,
        CancellationToken cancellationToken)
    {
        var prompt = BuildBatchPrompt(leagueName, matches);
        var jsonResponse = await CallGeminiJsonAsync(prompt, cancellationToken);

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var response = JsonSerializer.Deserialize<GeminiBatchAnalysisResponse>(jsonResponse, options);
            return response ?? new GeminiBatchAnalysisResponse();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse Gemini batch response for {League}", leagueName);
            return new GeminiBatchAnalysisResponse();
        }
    }

    private string BuildBatchPrompt(string leagueName, List<UpcomingMatchDto> matches)
    {
        // Serialize COMPLETE match data - let Gemini see everything
        var matchesJson = JsonSerializer.Serialize(matches, new JsonSerializerOptions 
        { 
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        return $@"
Role: Expert Football Analyst & Sports Bettor
League: {leagueName}
Task: Analyze ALL {matches.Count} matches in this batch and provide predictions.
Identify patterns where ML models might fail (draws, upsets).dont keep your focus on ml model take poisson and monte carlo as well in account.
keep your primary focus on over25 or btts secondary you can do hdw predictions.
Return clear JSON predictions.

Complete Match Data (including all stats, ML predictions, analytics, traps, odds):
{matchesJson}

Instructions (STRICT ENSEMBLE LOGIC):
1. **ENSEMBLE VOTING (CRITICAL)**:
   - You are the ""Judge"". You must weigh conflicting evidence.
   - **ML Model**: Good at general patterns, but can be overconfident.
   - **Poisson/Dixon-Coles**: The ""Math"" voice. If it says < 50% for Over 2.5, LISTEN TO IT.
   - **Monte Carlo**: The ""Simulation"" voice. If it says < 50% for Over 2.5, LISTEN TO IT.
   - **Market Odds**: The ""Smart Money"". If Odds > 1.90 for Over 2.5, the market is skeptical.

2. **MANDATORY DISAGREEMENT CHECK**:
   - IF ML says ""Over 2.5"" (High Confidence)
   - BUT (Poisson < 50% OR Monte Carlo < 50% OR Odds > 1.90)
   - THEN you MUST **DOWNGRADE CONFIDENCE** significantly (to < 0.60) or switch prediction to ""Under/Draw"".
   - *Reasoning must state: ""ML disagreed with Math/Market""*

3. **Low Score (1-0/0-1) Trap Detection**:
   - Watch out for games with low Combined Avg Goals (< 2.5).
   - Do not blindly follow ML if team form is weak (Avg Goals For < 1.0).

4. **Synthesize**:
   - High Confidence (> 0.70) REQUIREMENT: ML + Math + Market ALL agree.
   - If models conflict -> Low Confidence (0.50 - 0.60).
   - Explain the conflict in the reasoning.

5. **Output**:
   - Be concise. Focus on the *consensus* (or lack thereof).

Output JSON (EXACTLY this structure):
{{
  ""analyses"": [
    {{
      ""matchId"": ""HomeTeam-AwayTeam-Date"",
      ""analysis"": ""Comprehensive match analysis considering all data. 4-6 sentences"",
      ""prediction"": ""Over 2.5 Goals"" OR ""BTTS"" OR ""Home Win"" OR ""Away Win"" OR ""Draw"",
      ""confidence"": 0.75,
      ""reasoning"": ""Why this prediction based on the complete dataset""
    }}
  ]
}}

CRITICAL: 
- Return analysis for ALL {matches.Count} matches
- Match matchId format: ""HomeTeam-AwayTeam-Date""
- Use the COMPLETE dataset to make informed decisions
- Explain any conflicts between different data sources";
    }

    private async Task<string> CallGeminiJsonAsync(string prompt, CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            contents = new[] {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                responseMimeType = "application/json"
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

        var response = await _httpClient.PostAsync(url, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Gemini API Failed: {StatusCode} - {Content}", response.StatusCode, errorContent);
            response.EnsureSuccessStatusCode();
        }

        var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseString);

        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        return text ?? "{}";
    }
}
