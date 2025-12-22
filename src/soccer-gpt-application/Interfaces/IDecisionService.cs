using System.Text.Json.Serialization;

namespace soccer_gpt_application.Interfaces;

public interface IDecisionService
{
    BettingDecisionDto MakeDecision(
        MatchProbabilitiesDto probabilities, 
        H2HAnalysisDto h2hAnalysis,
        MatchOddsDto? odds = null);
}

public class BettingDecisionDto
{
    [JsonPropertyName("selected_market")]
    public string SelectedMarket { get; set; } = "No Bet";
    
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
    
    [JsonPropertyName("expected_value")]
    public double ExpectedValue { get; set; }
    
    [JsonPropertyName("is_high_confidence")]
    public bool IsHighConfidence { get; set; }
    
    [JsonPropertyName("has_h2h_support")]
    public bool HasH2HSupport { get; set; }
    
    [JsonPropertyName("reasons")]
    public List<string> Reasons { get; set; } = new();
}
