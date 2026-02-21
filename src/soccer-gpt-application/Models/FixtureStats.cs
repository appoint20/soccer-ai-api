namespace soccer_gpt_application.Models;

/// <summary>
/// Statistics for a team in a fixture from API-Football
/// </summary>
public record FixtureStats
{
    public int TotalShots { get; init; }
    public int ShotsOnGoal { get; init; }
    public int? BallPossession { get; init; }
    public int? PassesAccurate { get; init; }
    public double? ExpectedGoals { get; init; }
}
