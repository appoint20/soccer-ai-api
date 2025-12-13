
using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models;

public class TeamStatsResponse
{
    [JsonPropertyName("response")]
    public TeamStatsData? Response { get; set; }
}

public record TeamStatsData
{
    [JsonPropertyName("league")]
    public LeagueInfo? League { get; set; }

    [JsonPropertyName("team")]
    public TeamInfo? Team { get; set; }

    [JsonPropertyName("form")]
    public string? Form { get; set; }

    [JsonPropertyName("fixtures")]
    public FixturesStats? Fixtures { get; set; }

    [JsonPropertyName("goals")]
    public GoalsStats? Goals { get; set; }
}

public class LeagueInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("season")]
    public int Season { get; set; }
}

public class TeamInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class FixturesStats
{
    [JsonPropertyName("played")]
    public StatDetail? Played { get; set; }
    [JsonPropertyName("wins")]
    public StatDetail? Wins { get; set; }
    [JsonPropertyName("draws")]
    public StatDetail? Draws { get; set; }
    [JsonPropertyName("loses")]
    public StatDetail? Loses { get; set; }
}

public class StatDetail
{
    [JsonPropertyName("home")]
    public int Home { get; set; }
    [JsonPropertyName("away")]
    public int Away { get; set; }
    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class GoalsStats
{
    [JsonPropertyName("for")]
    public GoalsDetail? For { get; set; }
    [JsonPropertyName("against")]
    public GoalsDetail? Against { get; set; }
}

public class GoalsDetail
{
    [JsonPropertyName("total")]
    public StatDetail? Total { get; set; }
    
    [JsonPropertyName("average")]
    public StatAverage? Average { get; set; }
}

public class StatAverage
{
    [JsonPropertyName("home")]
    public string? Home { get; set; }
    [JsonPropertyName("away")]
    public string? Away { get; set; }
    [JsonPropertyName("total")]
    public string? Total { get; set; }
}
