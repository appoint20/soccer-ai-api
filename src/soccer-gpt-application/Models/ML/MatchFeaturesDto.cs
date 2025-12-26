using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models.ML;

/// <summary>
/// Comprehensive feature set for match analysis
/// Properly separated home/away context with volatility and momentum
/// </summary>
public class MatchFeaturesDto
{
    // Match Context
    [JsonPropertyName("home_team")]
    public string HomeTeam { get; set; } = string.Empty;
    
    [JsonPropertyName("away_team")]
    public string AwayTeam { get; set; } = string.Empty;
    
    [JsonPropertyName("league")]
    public string? League { get; set; }
    
    // === Attack Features (Home) ===
    [JsonPropertyName("home_attack_strength")]
    public double HomeAttackStrength { get; set; }
    
    [JsonPropertyName("home_attack_volatility")]
    public double HomeAttackVolatility { get; set; }
    
    [JsonPropertyName("home_scoring_efficiency")]
    public double HomeScoringEfficiency { get; set; }
    
    [JsonPropertyName("home_goals_last_5")]
    public double HomeGoalsLast5 { get; set; }
    
    [JsonPropertyName("home_goals_last_10")]
    public double HomeGoalsLast10 { get; set; }
    
    // === Attack Features (Away) ===
    [JsonPropertyName("away_attack_strength")]
    public double AwayAttackStrength { get; set; }
    
    [JsonPropertyName("away_attack_volatility")]
    public double AwayAttackVolatility { get; set; }
    
    [JsonPropertyName("away_scoring_efficiency")]
    public double AwayScoringEfficiency { get; set; }
    
    [JsonPropertyName("away_goals_last_5")]
    public double AwayGoalsLast5 { get; set; }
    
    [JsonPropertyName("away_goals_last_10")]
    public double AwayGoalsLast10 { get; set; }
    
    // === Defense Features (Home) ===
    [JsonPropertyName("home_defense_strength")]
    public double HomeDefenseStrength { get; set; }
    
    [JsonPropertyName("home_defense_volatility")]
    public double HomeDefenseVolatility { get; set; }
    
    [JsonPropertyName("home_clean_sheet_rate")]
    public double HomeCleanSheetRate { get; set; }
    
    [JsonPropertyName("home_conceded_last_5")]
    public double HomeConcededLast5 { get; set; }
    
    // === Defense Features (Away) ===
    [JsonPropertyName("away_defense_strength")]
    public double AwayDefenseStrength { get; set; }
    
    [JsonPropertyName("away_defense_volatility")]
    public double AwayDefenseVolatility { get; set; }
    
    [JsonPropertyName("away_clean_sheet_rate")]
    public double AwayCleanSheetRate { get; set; }
    
    [JsonPropertyName("away_conceded_last_5")]
    public double AwayConcededLast5 { get; set; }
    
    // === Form & Momentum ===
    [JsonPropertyName("home_momentum")]
    public double HomeMomentum { get; set; }
    
    [JsonPropertyName("away_momentum")]
    public double AwayMomentum { get; set; }
    
    [JsonPropertyName("momentum_gap")]
    public double MomentumGap { get; set; }
    
    [JsonPropertyName("home_fail_to_score_rate")]
    public double HomeFailToScoreRate { get; set; }
    
    [JsonPropertyName("away_fail_to_score_rate")]
    public double AwayFailToScoreRate { get; set; }
    
    [JsonPropertyName("home_form_trend")]
    public string HomeFormTrend { get; set; } = string.Empty; // "Improving", "Declining", "Stable"
    
    [JsonPropertyName("away_form_trend")]
    public string AwayFormTrend { get; set; } = string.Empty;
    
    // === League Context ===
    [JsonPropertyName("league_avg_goals")]
    public double LeagueAvgGoals { get; set; }
    
    [JsonPropertyName("league_goal_volatility")]
    public double LeagueGoalVolatility { get; set; }
    
    [JsonPropertyName("home_vs_league_attack")]
    public double HomeVsLeagueAttack { get; set; }
    
    [JsonPropertyName("away_vs_league_attack")]
    public double AwayVsLeagueAttack { get; set; }
    
    [JsonPropertyName("home_vs_league_defense")]
    public double HomeVsLeagueDefense { get; set; }
    
    [JsonPropertyName("away_vs_league_defense")]
    public double AwayVsLeagueDefense { get; set; }
    
    // === Match Context ===
    [JsonPropertyName("home_opponent_quality_avg")]
    public double HomeOpponentQualityAvg { get; set; }
    
    [JsonPropertyName("away_opponent_quality_avg")]
    public double AwayOpponentQualityAvg { get; set; }
    
    [JsonPropertyName("home_rest_days")]
    public int? HomeRestDays { get; set; }
    
    [JsonPropertyName("away_rest_days")]
    public int? AwayRestDays { get; set; }
    
    // === Derived Metrics ===
    [JsonPropertyName("expected_total_goals")]
    public double ExpectedTotalGoals { get; set; }
    
    [JsonPropertyName("match_volatility_index")]
    public double MatchVolatilityIndex { get; set; }
    
    [JsonPropertyName("quality_differential")]
    public double QualityDifferential { get; set; }
}

/// <summary>
/// Attack feature subset
/// </summary>
public class AttackFeatures
{
    public double Strength { get; set; }
    public double Volatility { get; set; }
    public double Last5Avg { get; set; }
    public double Last10Avg { get; set; }
    public double ScoringEfficiency { get; set; }
}

/// <summary>
/// Defense feature subset
/// </summary>
public class DefenseFeatures
{
    public double Strength { get; set; }
    public double Volatility { get; set; }
    public double CleanSheetRate { get; set; }
    public double Last5Avg { get; set; }
}

/// <summary>
/// League context for normalization
/// </summary>
public class LeagueContext
{
    public string League { get; set; } = string.Empty;
    public double AvgGoals { get; set; }
    public double GoalVolatility { get; set; }
    public double AvgHomeGoals { get; set; }
    public double AvgAwayGoals { get; set; }
}
