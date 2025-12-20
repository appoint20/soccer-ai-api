namespace soccer_gpt_application.Models;

/// <summary>
/// Gemini analysis for a single match
/// </summary>
public record GeminiMatchAnalysis
{
    public string MatchKey { get; init; } = string.Empty; // "Liverpool-ManUnited"
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string League { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string Analysis { get; init; } = string.Empty;
    public string Prediction { get; init; } = string.Empty;
    public double ConfidenceLevel { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTime GeneratedAt { get; init; }
}

/// <summary>
/// Cache structure for Gemini analyses
/// </summary>
public record GeminiAnalysisCache
{
    public string Version { get; init; } = "1.0";
    public DateTime LastUpdated { get; init; }
    public int TotalMatches { get; init; }
    public Dictionary<string, GeminiMatchAnalysis> Analyses { get; init; } = new();
}

/// <summary>
/// Batch request for Gemini to analyze multiple matches at once
/// </summary>
public record GeminiBatchMatchRequest
{
    public string LeagueName { get; init; } = string.Empty;
    public List<GeminiMatchInput> Matches { get; init; } = new();
}

public record GeminiMatchInput
{
    public string MatchId { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public object? HomeStats { get; init; }
    public object? AwayStats { get; init; }
    public object? MlPrediction { get; init; }
    public object? MathProbabilities { get; init; }
}

/// <summary>
/// Gemini batch response for multiple matches
/// </summary>
public record GeminiBatchAnalysisResponse
{
    public List<GeminiBatchMatchAnalysis> Analyses { get; init; } = new();
}

public record GeminiBatchMatchAnalysis
{
    public string MatchId { get; init; } = string.Empty;
    public string Analysis { get; init; } = string.Empty;
    public string Prediction { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public string Reasoning { get; init; } = string.Empty;
}
