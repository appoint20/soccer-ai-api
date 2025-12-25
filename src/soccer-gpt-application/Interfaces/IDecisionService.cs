using System.Text.Json.Serialization;
using soccer_gpt_application.Models.ML;

namespace soccer_gpt_application.Interfaces;

public interface IDecisionService
{
    BettingDecisionDto MakeDecision(
        string homeTeam,
        string awayTeam,
        MatchProbabilitiesDto probabilities, 
        List<HistoricalMatchDto> history,
        string? league = null,
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
