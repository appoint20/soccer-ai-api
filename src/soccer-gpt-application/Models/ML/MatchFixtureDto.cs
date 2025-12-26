namespace soccer_gpt_application.Models.ML;

/// <summary>
/// Fixture information for match analysis
/// </summary>
public class MatchFixtureDto
{
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public string? League { get; set; }
    public DateTime? MatchDate { get; set; }
    public soccer_gpt_application.Interfaces.MatchOddsDto? Odds { get; set; }
}
