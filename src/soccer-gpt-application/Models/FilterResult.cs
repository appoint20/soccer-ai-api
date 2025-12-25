namespace soccer_gpt_application.Models;

/// <summary>
/// Result of applying defensive filters
/// </summary>
public class FilterResult
{
    public bool IsAllowed { get; set; }
    public double FinalConfidence { get; set; }
    public List<string> Reasons { get; set; } = new();
    
    public static FilterResult Allow(double confidence, string reason = "")
    {
        return new FilterResult 
        { 
            IsAllowed = true, 
            FinalConfidence = confidence,
            Reasons = string.IsNullOrEmpty(reason) ? new() : new() { reason }
        };
    }
    
    public static FilterResult Block(string reason)
    {
        return new FilterResult 
        { 
            IsAllowed = false, 
            FinalConfidence = 0,
            Reasons = new() { reason }
        };
    }
    
    public static FilterResult Cap(double cappedConfidence, params string[] reasons)
    {
        return new FilterResult 
        { 
            IsAllowed = true, 
            FinalConfidence = cappedConfidence,
            Reasons = reasons.ToList()
        };
    }
}
