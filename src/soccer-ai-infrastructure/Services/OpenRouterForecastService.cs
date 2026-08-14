using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Infrastructure.Options;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Asks each configured model, through OpenRouter, for an independent goals
/// forecast built from the same statistics the pipeline saw.
///
/// The prompt deliberately shows each model the pipeline's own probabilities.
/// Hiding them would measure something less useful — whether a language model
/// can rediscover Dixon-Coles from summary statistics — when the question that
/// matters is whether it adds anything on top of what the pipeline already
/// knows. The instruction to move only when the statistics warrant it is what
/// keeps the comparison from collapsing into an echo.
///
/// Raw HTTP rather than the OpenAI SDK: a <c>ChatClient</c> binds one model per
/// instance, and the whole point here is running several against one payload.
/// </summary>
public sealed class OpenRouterForecastService : IMatchForecastService
{
    /// <summary>Named client so the timeout and headers are configured once, at registration.</summary>
    public const string HttpClientName = "openrouter";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenRouterOptions _options;
    private readonly ILogger<OpenRouterForecastService> _logger;
    private readonly string? _apiKey;

    public OpenRouterForecastService(
        IHttpClientFactory httpClientFactory,
        IOptions<OpenRouterOptions> options,
        ILogger<OpenRouterForecastService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;

        _apiKey = string.IsNullOrWhiteSpace(_options.ApiKey)
            ? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")
            : _options.ApiKey;

        if (!IsEnabled)
        {
            _logger.LogInformation(
                "[Forecast] Disabled — need OPENROUTER_API_KEY and at least one entry in OpenRouter:Models "
                + "(key {KeyState}, {ModelCount} model(s) configured).",
                string.IsNullOrWhiteSpace(_apiKey) ? "missing" : "present",
                _options.Models.Count);
        }
    }

    public bool IsEnabled =>
        _options.Enabled && !string.IsNullOrWhiteSpace(_apiKey) && _options.Models.Count > 0;

    public IReadOnlyList<string> Models => _options.Models;

    public async Task<IReadOnlyList<GoalsForecast>> ForecastAsync(
        MatchAnalysis analysis, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        if (!IsEnabled) return [];

        // Concurrent and independent: one model failing costs its own forecast,
        // not the others'.
        var tasks = _options.Models.Select(m => ForecastOneAsync(m, analysis, cancellationToken));
        var results = await Task.WhenAll(tasks);

        return [.. results.OfType<GoalsForecast>()];
    }

    private async Task<GoalsForecast?> ForecastOneAsync(
        string model, MatchAnalysis analysis, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);

            var request = new
            {
                model,
                max_tokens = _options.MaxTokens,
                messages = new object[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = BuildPrompt(analysis) },
                },
                response_format = new
                {
                    type = "json_schema",
                    json_schema = new { name = "goals_forecast", strict = true, schema = ForecastSchema },
                },
            };

            using var response = await client.PostAsJsonAsync("chat/completions", request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[Forecast] {Model} returned {Status} for fixture {FixtureId}: {Body}",
                    model, (int)response.StatusCode, analysis.Id, Trim(body));
                return null;
            }

            var content = ExtractContent(body);
            if (string.IsNullOrWhiteSpace(content))
            {
                _logger.LogWarning(
                    "[Forecast] {Model} returned no content for fixture {FixtureId}", model, analysis.Id);
                return null;
            }

            return Parse(model, content, analysis.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A measurement must never take down the sync that carries it.
            _logger.LogWarning(ex,
                "[Forecast] {Model} failed for fixture {FixtureId}", model, analysis.Id);
            return null;
        }
    }

    /// <summary>
    /// Pulls the assistant text out of the OpenAI-shaped envelope. Some models
    /// route their JSON through a tool call instead of the content field, so
    /// fall back to the first tool call's arguments before giving up.
    /// </summary>
    private static string? ExtractContent(string body)
    {
        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return null;

        if (!choices[0].TryGetProperty("message", out var message))
            return null;

        if (message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(content.GetString()))
        {
            return content.GetString();
        }

        if (message.TryGetProperty("tool_calls", out var toolCalls)
            && toolCalls.ValueKind == JsonValueKind.Array
            && toolCalls.GetArrayLength() > 0
            && toolCalls[0].TryGetProperty("function", out var fn)
            && fn.TryGetProperty("arguments", out var args))
        {
            return args.GetString();
        }

        return null;
    }

    private GoalsForecast? Parse(string model, string json, int fixtureId)
    {
        try
        {
            using var doc = JsonDocument.Parse(StripCodeFence(json));
            var root = doc.RootElement;

            return new GoalsForecast
            {
                Model = model,
                ExpectedGoals = Math.Clamp(root.GetProperty("expected_goals").GetDouble(), 0, 15),
                PredictedHomeGoals = Math.Clamp(root.GetProperty("predicted_home_goals").GetInt32(), 0, 15),
                PredictedAwayGoals = Math.Clamp(root.GetProperty("predicted_away_goals").GetInt32(), 0, 15),
                Over25Probability = Math.Clamp(root.GetProperty("over_2_5_probability").GetDouble(), 0, 1),
                BttsProbability = Math.Clamp(root.GetProperty("btts_probability").GetDouble(), 0, 1),
                Confidence = Math.Clamp(root.GetProperty("confidence").GetDouble(), 0, 1),
                Rationale = root.GetProperty("rationale").GetString() ?? "",
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Forecast] {Model} produced unparseable output for fixture {FixtureId}: {Json}",
                model, fixtureId, Trim(json));
            return null;
        }
    }

    /// <summary>
    /// Not every model on the gateway honours json_schema; some still wrap the
    /// object in a markdown fence. Cheap to tolerate, expensive to debug.
    /// </summary>
    private static string StripCodeFence(string text)
    {
        var t = text.Trim();
        if (!t.StartsWith("```", StringComparison.Ordinal)) return t;

        var firstNewline = t.IndexOf('\n');
        if (firstNewline < 0) return t;

        var end = t.LastIndexOf("```", StringComparison.Ordinal);
        return end <= firstNewline ? t[(firstNewline + 1)..] : t[(firstNewline + 1)..end].Trim();
    }

    private static string Trim(string s) => s.Length <= 500 ? s : s[..500] + "…";

    private const string SystemPrompt =
        """
        You forecast association-football goals outcomes. You are given the
        statistics and the probabilities a Dixon-Coles model already produced for
        this fixture.

        Your forecast is recorded and scored against that model on real results,
        so it is only worth something if it is your own. Where the statistics
        support the model, agreeing is the right answer. Where you see something
        the model does not price — a striker's finishing run against a defence
        that concedes chances, an H2H pattern that contradicts current form, a
        table position that changes how a side will set up — say so and move your
        numbers accordingly.

        Give probabilities you would stand behind, not hedged ones. Forecasting
        0.5 on everything scores badly and tells nobody anything. Equally, do not
        manufacture disagreement to look useful: unjustified movement away from a
        calibrated model is how a forecaster loses to it.

        Return only the JSON object described by the schema.
        """;

    private static string BuildPrompt(MatchAnalysis a)
    {
        var p = a.Prediction;
        return $"""
        FIXTURE
        {a.HomeTeam} vs {a.AwayTeam} — {a.League}, kickoff {a.Date:yyyy-MM-dd HH:mm} UTC

        MODEL PROBABILITIES (Dixon-Coles, odds-calibrated)
        Over 2.5 goals: {p?.Over25.Probability ?? 0:P1}
        Both teams score: {p?.BTTS.Probability ?? 0:P1}
        2-3 goals: {p?.TwoToThreeGoals.Probability ?? 0:P1}
        Home win: {p?.HomeWin.Probability ?? 0:P1} | Away win: {p?.AwayWin.Probability ?? 0:P1}

        MARKET ODDS
        Over 2.5: {a.OddsOver25?.ToString("0.00") ?? "n/a"} | Under 2.5: {a.OddsUnder25?.ToString("0.00") ?? "n/a"}
        BTTS yes: {a.OddsBttsYes?.ToString("0.00") ?? "n/a"}
        1X2: {a.OddsHomeWin?.ToString("0.00") ?? "n/a"} / {a.OddsDraw?.ToString("0.00") ?? "n/a"} / {a.OddsAwayWin?.ToString("0.00") ?? "n/a"}

        HOME — {a.HomeStats.Name}
        Rank {a.HomeStats.Rank}, {a.HomeStats.Points} pts, form {a.HomeStats.Form} ({a.HomeStats.FormPercentage}%)
        Last 3: {a.HomeStats.AvgGoalsScoredLast3:0.00} scored, {a.HomeStats.AvgGoalsConcededLast3:0.00} conceded
        BTTS rate {a.HomeStats.BTTSRateLast3:P0}, Over 2.5 rate {a.HomeStats.Over25RateLast3:P0}, clean sheets {a.HomeStats.CleanSheetRate:P0}

        AWAY — {a.AwayStats.Name}
        Rank {a.AwayStats.Rank}, {a.AwayStats.Points} pts, form {a.AwayStats.Form} ({a.AwayStats.FormPercentage}%)
        Last 3: {a.AwayStats.AvgGoalsScoredLast3:0.00} scored, {a.AwayStats.AvgGoalsConcededLast3:0.00} conceded
        BTTS rate {a.AwayStats.BTTSRateLast3:P0}, Over 2.5 rate {a.AwayStats.Over25RateLast3:P0}, clean sheets {a.AwayStats.CleanSheetRate:P0}

        Forecast the goals outcome. Keep the rationale under 60 words and point at
        the specific numbers that moved you.
        """;
    }

    private static readonly JsonElement ForecastSchema = JsonSerializer.Deserialize<JsonElement>(
        """
        {
          "type": "object",
          "properties": {
            "expected_goals":       { "type": "number",  "description": "Expected total goals in the match." },
            "predicted_home_goals": { "type": "integer", "description": "Most likely home score." },
            "predicted_away_goals": { "type": "integer", "description": "Most likely away score." },
            "over_2_5_probability": { "type": "number",  "description": "Probability of 3+ total goals, 0 to 1." },
            "btts_probability":     { "type": "number",  "description": "Probability both teams score, 0 to 1." },
            "confidence":           { "type": "number",  "description": "Your confidence in this forecast, 0 to 1." },
            "rationale":            { "type": "string",  "description": "Under 60 words, citing the numbers that moved you." }
          },
          "required": [
            "expected_goals", "predicted_home_goals", "predicted_away_goals",
            "over_2_5_probability", "btts_probability", "confidence", "rationale"
          ],
          "additionalProperties": false
        }
        """);
}
