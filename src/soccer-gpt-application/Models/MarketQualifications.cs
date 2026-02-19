namespace soccer_gpt_application.Models;

/// <summary>
/// Market qualification results based on analysis
/// </summary>
public sealed class MarketQualifications
{
    public MarketQualification Draw { get; init; } = MarketQualification.NotQualified;
    public MarketQualification BTTS { get; init; } = MarketQualification.NotQualified;
    public MarketQualification Over25 { get; init; } = MarketQualification.NotQualified;
    public MarketQualification TwoToThreeGoals { get; init; } = MarketQualification.NotQualified;
    public MarketQualification HomeWin { get; init; } = MarketQualification.NotQualified;
    public MarketQualification AwayWin { get; init; } = MarketQualification.NotQualified;
}

public sealed class MarketQualification
{
    public bool IsQualified { get; init; }
    public double Confidence { get; init; }
    public string Reasoning { get; init; } = string.Empty;
    
    public static MarketQualification NotQualified => new() 
    { 
        IsQualified = false, 
        Confidence = 0,
        Reasoning = "Below threshold"
    };
    
    public static MarketQualification Qualify(double confidence, string reasoning) => new()
    {
        IsQualified = true,
        Confidence = Math.Round(confidence, 3),
        Reasoning = reasoning
    };
    
    public static MarketQualification Evaluate(double confidence, double threshold, string marketName) => new()
    {
        IsQualified = confidence >= threshold,
        Confidence = Math.Round(confidence, 3),
        Reasoning = confidence >= threshold 
            ? $"{marketName} qualified with {confidence:P0} confidence" 
            : $"{marketName} below {threshold:P0} threshold"
    };
}
