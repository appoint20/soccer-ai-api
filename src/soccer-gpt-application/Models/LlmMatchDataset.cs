
using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models.Llm;

public class LlmMatchDataset
{
    [JsonPropertyName("fixture")]
    public LlmFixture Fixture { get; set; } = new();

    [JsonPropertyName("teams")]
    public LlmTeams Teams { get; set; } = new();

    [JsonPropertyName("ml_outputs")]
    public LlmMlOutputs MlOutputs { get; set; } = new();

    [JsonPropertyName("aggregated_signals")]
    public LlmAggregatedSignals AggregatedSignals { get; set; } = new();

    [JsonPropertyName("risk_profile")]
    public LlmRiskProfile RiskProfile { get; set; } = new();

    [JsonPropertyName("constraints")]
    public LlmConstraints Constraints { get; set; } = new();
}

public class LlmFixture
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }
    [JsonPropertyName("league")]
    public string League { get; set; } = string.Empty;
    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;
    [JsonPropertyName("season")]
    public int Season { get; set; }
    [JsonPropertyName("kickoffUtc")]
    public string KickoffUtc { get; set; } = string.Empty;
}

public class LlmTeams
{
    [JsonPropertyName("home")]
    public string Home { get; set; } = string.Empty;
    [JsonPropertyName("away")]
    public string Away { get; set; } = string.Empty;
}

public class LlmMlOutputs
{
    [JsonPropertyName("match_outcome")]
    public LlmMatchOutcome MatchOutcome { get; set; } = new();
    
    [JsonPropertyName("goals_market")]
    public LlmGoalsMarket GoalsMarket { get; set; } = new();
    
    [JsonPropertyName("btts")]
    public LlmBtts Btts { get; set; } = new();
    
    [JsonPropertyName("expected_goals")]
    public LlmExpectedGoals ExpectedGoals { get; set; } = new();

    [JsonPropertyName("model_confidence")]
    public double ModelConfidence { get; set; }
}

public class LlmMatchOutcome
{
    [JsonPropertyName("home_win")]
    public double HomeWin { get; set; }
    [JsonPropertyName("draw")]
    public double Draw { get; set; }
    [JsonPropertyName("away_win")]
    public double AwayWin { get; set; }
}

public class LlmGoalsMarket
{
    [JsonPropertyName("over_1_5")]
    public double Over1_5 { get; set; }
    [JsonPropertyName("over_2_5")]
    public double Over2_5 { get; set; }
    [JsonPropertyName("under_3_5")]
    public double Under3_5 { get; set; }
}

public class LlmBtts
{
    [JsonPropertyName("yes")]
    public double Yes { get; set; }
    [JsonPropertyName("no")]
    public double No { get; set; }
}

public class LlmExpectedGoals
{
    [JsonPropertyName("home")]
    public double Home { get; set; }
    [JsonPropertyName("away")]
    public double Away { get; set; }
    [JsonPropertyName("total")]
    public double Total { get; set; }
}

public class LlmAggregatedSignals
{
    [JsonPropertyName("dominance")]
    public string Dominance { get; set; } = string.Empty;
    [JsonPropertyName("goal_environment")]
    public string GoalEnvironment { get; set; } = string.Empty;
    [JsonPropertyName("tempo")]
    public string Tempo { get; set; } = string.Empty;
    [JsonPropertyName("variance")]
    public string Variance { get; set; } = string.Empty;
    [JsonPropertyName("home_not_losing")]
    public bool HomeNotLosing { get; set; }
    [JsonPropertyName("btts_risk")]
    public string BttsRisk { get; set; } = string.Empty;
    [JsonPropertyName("late_goal_risk")]
    public string LateGoalRisk { get; set; } = string.Empty;
}

public class LlmRiskProfile
{
    [JsonPropertyName("volatility")]
    public string Volatility { get; set; } = string.Empty;
    [JsonPropertyName("historical_stability")]
    public string HistoricalStability { get; set; } = string.Empty;
    [JsonPropertyName("league_reliability")]
    public string LeagueReliability { get; set; } = string.Empty;
    [JsonPropertyName("data_quality")]
    public string DataQuality { get; set; } = string.Empty;
}

public class LlmConstraints
{
    [JsonPropertyName("min_probability")]
    public double MinProbability { get; set; }
    [JsonPropertyName("allowed_markets")]
    public List<string> AllowedMarkets { get; set; } = [];
    [JsonPropertyName("forbidden_markets")]
    public List<string> ForbiddenMarkets { get; set; } = [];
    [JsonPropertyName("max_selections")]
    public int MaxSelections { get; set; }
    [JsonPropertyName("risk_mode")]
    public string RiskMode { get; set; } = string.Empty;
}
