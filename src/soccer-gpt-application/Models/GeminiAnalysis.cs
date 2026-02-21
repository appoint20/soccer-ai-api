namespace soccer_gpt_application.Models;

public sealed class GeminiAnalysis
{
    public string Recommendation { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public string Reasoning { get; init; } = string.Empty;
    public string Analysis { get; init; } = string.Empty;
    public bool IsTrap { get; init; }
}
