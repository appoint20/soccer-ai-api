
using System.Text.Json.Serialization;

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
    
    [JsonPropertyName("odds")]
    public MatchOdds? Odds { get; init; }

    [JsonPropertyName("h2h_analysis")]
    public H2HAnalysis? H2HAnalysis { get; init; }
    
    [JsonPropertyName("home_team_stats")]
    public TeamStatsSummary? HomeTeamStats { get; init; }
    
    [JsonPropertyName("away_team_stats")]
    public TeamStatsSummary? AwayTeamStats { get; init; }
}

public record MatchOdds
{
    [JsonPropertyName("home_win")]
    public decimal HomeWin { get; init; }
    [JsonPropertyName("draw")]
    public decimal Draw { get; init; }
    [JsonPropertyName("away_win")]
    public decimal AwayWin { get; init; }
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
