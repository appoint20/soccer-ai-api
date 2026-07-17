using System.Text.Json.Serialization;
using SoccerAi.Application.Features.Combinations;

namespace SoccerAi.Application.Models;

public class AiBatchItem
{
    [JsonPropertyName("fixtureId")]
    public int FixtureId { get; set; }
    
    [JsonPropertyName("homeTeam")]
    public string HomeTeam { get; set; } = string.Empty;
    
    [JsonPropertyName("awayTeam")]
    public string AwayTeam { get; set; } = string.Empty;
    
    [JsonPropertyName("league")]
    public string League { get; set; } = string.Empty;
    [JsonPropertyName("homeStats")]
    public TeamStats HomeStats { get; set; } = TeamStats.Empty;
    
    [JsonPropertyName("awayStats")]
    public TeamStats AwayStats { get; set; } = TeamStats.Empty;

    [JsonPropertyName("homeGoalAvg")]
    public double HomeGoalAvg { get; set; }
    
    [JsonPropertyName("awayGoalAvg")]
    public double AwayGoalAvg { get; set; }

    [JsonPropertyName("homeWinProb")]
    public double ModelHomeWin { get; set; }
    
    [JsonPropertyName("drawProb")]
    public double ModelDraw { get; set; }
    public double ModelGoals23 { get; set; }
    
    [JsonPropertyName("awayWinProb")]
    public double ModelAwayWin { get; set; }

    [JsonPropertyName("over25Prob")]
    public double ModelOver25 { get; set; }
    
    [JsonPropertyName("bttsProb")]
    public double ModelBTTS { get; set; }

    [JsonPropertyName("oddsHomeWin")]
    public double? OddsHomeWin { get; set; }
    
    [JsonPropertyName("oddsDraw")]
    public double? OddsDraw { get; set; }
    
    [JsonPropertyName("oddsAwayWin")]
    public double? OddsAwayWin { get; set; }
    
    [JsonPropertyName("oddsOver25")]
    public double? OddsOver25 { get; set; }
    
    [JsonPropertyName("oddsBtts")]
    public double? OddsBTTS { get; set; }

    public double? HomeElo { get; set; }
    public double? AwayElo { get; set; }
}
