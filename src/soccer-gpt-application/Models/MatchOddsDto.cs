using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models;

public class MatchOddsDto
{
    [JsonPropertyName("home_win")]
    public decimal HomeWin { get; set; }
    
    [JsonPropertyName("draw")]
    public decimal Draw { get; set; }
    
    [JsonPropertyName("away_win")]
    public decimal AwayWin { get; set; }
    
    [JsonPropertyName("over_2_5")]
    public decimal Over25 { get; set; }
    
    [JsonPropertyName("under_2_5")]
    public decimal Under25 { get; set; }
    
    [JsonPropertyName("btts_yes")]
    public decimal BttsYes { get; set; }
}
