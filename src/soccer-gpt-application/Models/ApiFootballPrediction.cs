
using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models;

public class ApiFootballPrediction
{
    [JsonPropertyName("fixture_id")]
    public int FixtureId { get; set; }
    
    [JsonPropertyName("home_team")]
    public string HomeTeam { get; set; } = string.Empty;
    
    [JsonPropertyName("away_team")]
    public string AwayTeam { get; set; } = string.Empty;

    [JsonPropertyName("api_prediction")]
    public ApiPredictionData ApiPrediction { get; set; } = new();

    [JsonPropertyName("teams_comparison")]
    public ApiTeamsComparison TeamsComparison { get; set; } = new();
}

public class ApiPredictionData
{
    [JsonPropertyName("winner")]
    public ApiWinner Winner { get; set; } = new();
    
    [JsonPropertyName("advice")]
    public string Advice { get; set; } = string.Empty;

    [JsonPropertyName("percent")]
    public ApiPercent Percent { get; set; } = new();
    
    [JsonPropertyName("under_over")]
    public string UnderOver { get; set; } = string.Empty;

    [JsonPropertyName("win_or_draw")]
    public bool WinOrDraw { get; set; }

    // "goals": { "home": "-3.5", "away": "-2.5" } 
    [JsonPropertyName("goals")]
    public Dictionary<string, object> Goals { get; set; } = new(); 
}

public class ApiWinner
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("comment")]
    public string Comment { get; set; } = string.Empty;
}

public class ApiPercent
{
    [JsonPropertyName("home")]
    public string Home { get; set; } = string.Empty;
    [JsonPropertyName("draw")]
    public string Draw { get; set; } = string.Empty;
    [JsonPropertyName("away")]
    public string Away { get; set; } = string.Empty;
}

public class ApiTeamsComparison
{
    // "att": { "home": "41%", "away": "59%" }
    [JsonPropertyName("att")]
    public ApiComparisonAtt Att { get; set; } = new();
    
    [JsonPropertyName("def")]
    public ApiComparisonDef Def { get; set; } = new();
    
    [JsonPropertyName("goals")]
    public ApiComparisonGoals Goals { get; set; } = new();
}

public class ApiComparisonAtt
{
    [JsonPropertyName("home")]
    public string Home { get; set; } = string.Empty;
    [JsonPropertyName("away")]
    public string Away { get; set; } = string.Empty;
}

public class ApiComparisonDef
{
    [JsonPropertyName("home")]
    public string Home { get; set; } = string.Empty;
    [JsonPropertyName("away")]
    public string Away { get; set; } = string.Empty;
}

public class ApiComparisonGoals
{
    [JsonPropertyName("home")]
    public string Home { get; set; } = string.Empty;
    [JsonPropertyName("away")]
    public string Away { get; set; } = string.Empty;
}
