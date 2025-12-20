
using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models;

public class ApiFixtureResponse
{
    [JsonPropertyName("response")]
    public List<ApiFixture> Response { get; set; } = [];
}

public class ApiFixture
{
    [JsonPropertyName("fixture")]
    public ApiFixtureInfo Fixture { get; set; } = new();
    
    [JsonPropertyName("league")]
    public ApiLeagueInfo League { get; set; } = new();
    
    [JsonPropertyName("teams")]
    public ApiTeamsInfo Teams { get; set; } = new();

    [JsonPropertyName("goals")]
    public ApiGoalsInfo Goals { get; set; } = new();
}

public class ApiGoalsInfo
{
    [JsonPropertyName("home")]
    public int? Home { get; set; }
    
    [JsonPropertyName("away")]
    public int? Away { get; set; }
}

public class ApiFixtureInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty; // ISO 8601
}

public class ApiLeagueInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class ApiTeamsInfo
{
    [JsonPropertyName("home")]
    public ApiTeamInfo Home { get; set; } = new();
    
    [JsonPropertyName("away")]
    public ApiTeamInfo Away { get; set; } = new();
}

public class ApiTeamInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
