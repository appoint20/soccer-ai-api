using System.Text.Json.Serialization;

namespace soccer_gpt_application.Interfaces;

public interface IAdvancedStatsService
{
    Task<AdvancedAnalyticsDto> CalculateAnalyticsAsync(string homeTeam, string awayTeam, List<soccer_gpt_application.Interfaces.HistoricalMatchDto> allHistory);
}

public class AdvancedAnalyticsDto
{
    [JsonPropertyName("probabilities")]
    public MatchProbabilitiesDto Probabilities { get; set; } = new();

    [JsonPropertyName("streak_analysis")]
    public StreakAnalysisDto StreakAnalysis { get; set; } = new();
}

public class MatchProbabilitiesDto
{
    [JsonPropertyName("home_win")]
    public double HomeWin { get; set; }
    
    [JsonPropertyName("draw")]
    public double Draw { get; set; }
    
    [JsonPropertyName("away_win")]
    public double AwayWin { get; set; }
    
    [JsonPropertyName("over_1_5")]
    public double Over15 { get; set; }
    
    [JsonPropertyName("over_2_5")]
    public double Over25 { get; set; }
    
    [JsonPropertyName("btts")]
    public double BTTS { get; set; }
}

public class StreakAnalysisDto
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "Neutral"; 
    
    [JsonPropertyName("reversion_index")]
    public double ReversionIndex { get; set; }
    
    [JsonPropertyName("monte_carlo_confidence")]
    public double MonteCarloConfidence { get; set; }

    // Expanded Edges
    [JsonPropertyName("edge_home_win")]
    public double EdgeHomeWin { get; set; }
    
    [JsonPropertyName("edge_draw")]
    public double EdgeDraw { get; set; }
    
    [JsonPropertyName("edge_away_win")]
    public double EdgeAwayWin { get; set; }
    
    [JsonPropertyName("edge_over_1_5")]
    public double EdgeOver15 { get; set; }

    [JsonPropertyName("edge_over_2_5")]
    public double EdgeOver25 { get; set; }
    
    [JsonPropertyName("edge_btts")]
    public double EdgeBTTS { get; set; }
}
