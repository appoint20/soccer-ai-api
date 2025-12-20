
using System.Text.Json.Serialization;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_application.Models;

public record UpcomingMatchDto
{
    [JsonPropertyName("date")]
    public string Date { get; init; } = string.Empty;

    [JsonPropertyName("time")]
    public string Time { get; init; } = string.Empty;

    [JsonPropertyName("home_team")]
    public string HomeTeam { get; init; } = string.Empty;

    [JsonPropertyName("away_team")]
    public string AwayTeam { get; init; } = string.Empty;

    [JsonPropertyName("league")]
    public string League { get; init; } = string.Empty;
    
    [JsonPropertyName("league_name")]
    public string LeagueName { get; init; } = string.Empty;
    
    [JsonPropertyName("odds")]
    public MatchOdds? Odds { get; init; }

    [JsonPropertyName("h2h_analysis")]
    public H2HAnalysis? H2HAnalysis { get; init; }
    
    [JsonPropertyName("home_team_stats")]
    public RichTeamStatsDto? HomeTeamStats { get; init; }
    
    [JsonPropertyName("away_team_stats")]
    public RichTeamStatsDto? AwayTeamStats { get; init; }

    [JsonPropertyName("advanced_analytics")]
    public AdvancedAnalyticsDto? AdvancedAnalytics { get; init; }

    [JsonPropertyName("traps")]
    public List<string> Traps { get; init; } = new();

    [JsonPropertyName("ml_prediction")]
    public soccer_gpt_application.Models.ML.MatchPredictionOutput? MlPrediction { get; init; }

    [JsonPropertyName("gemini")]
    public GeminiAnalysisDto? Gemini { get; init; }
}

public record TeamScheduleDto
{
    [JsonPropertyName("last_matches")]
    public List<SimpleMatchDto> LastMatches { get; init; } = new();
    
    [JsonPropertyName("next_matches")]
    public List<SimpleMatchDto> NextMatches { get; init; } = new();
}

public record SimpleMatchDto
{
    [JsonPropertyName("date")]
    public string Date { get; init; } = string.Empty;
    [JsonPropertyName("opponent")]
    public string Opponent { get; init; } = string.Empty;
    [JsonPropertyName("competition")]
    public string Competition { get; init; } = string.Empty;
    [JsonPropertyName("score")]
    public string Score { get; init; } = string.Empty; // "2-1" or "-"
}

public record MatchOdds
{
    [JsonPropertyName("home_win")]
    public decimal HomeWin { get; init; }
    [JsonPropertyName("draw")]
    public decimal Draw { get; init; }
    [JsonPropertyName("away_win")]
    public decimal AwayWin { get; init; }

    [JsonPropertyName("over_2_5")]
    public decimal Over25 { get; init; }

    [JsonPropertyName("under_2_5")]
    public decimal Under25 { get; init; }

    [JsonPropertyName("btts_yes")]
    public decimal BttsYes { get; init; }
}

public record H2HAnalysis
{
    [JsonPropertyName("home_wins_last_5")]
    public int HomeWinsLast5 { get; init; }
    [JsonPropertyName("away_wins_last_5")]
    public int AwayWinsLast5 { get; init; }
    [JsonPropertyName("draws_last_5")]
    public int DrawsLast5 { get; init; }
    [JsonPropertyName("status")]
    public string Status { get; init; } = "Unknown";
    
    [JsonPropertyName("avg_goals_home")]
    public double AvgGoalsHome { get; init; }
    
    [JsonPropertyName("avg_goals_away")]
    public double AvgGoalsAway { get; init; }

    [JsonPropertyName("form_home_last_5")]
    public string FormHomeLast5 { get; init; } = string.Empty;
    
    [JsonPropertyName("form_away_last_5")]
    public string FormAwayLast5 { get; init; } = string.Empty;
}

public record TeamStatsSummary
{
    [JsonPropertyName("form")]
    public string Form { get; init; } = string.Empty;
    
    [JsonPropertyName("goals_scored_avg")]
    public double GoalsScoredAvg { get; init; }
    
    [JsonPropertyName("goals_conceded_avg")]
    public double GoalsConcededAvg { get; init; }
}

public record GeminiAnalysisDto
{
    [JsonPropertyName("analysis")]
    public string Analysis { get; init; } = string.Empty;
    
    [JsonPropertyName("prediction")]
    public string Prediction { get; init; } = string.Empty;
    
    [JsonPropertyName("confidence_level")]
    public double ConfidenceLevel { get; init; }
    
    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}
