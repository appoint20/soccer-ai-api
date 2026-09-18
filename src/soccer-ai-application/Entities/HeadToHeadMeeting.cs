namespace SoccerAi.Application.Entities;

/// <summary>
/// A finished meeting between two teams, fetched from the provider's
/// head-to-head endpoint rather than from a league we sync.
/// </summary>
/// <remarks>
/// These are deliberately NOT stored as fixtures. A fixture row feeds team
/// form, the model's training set and every coverage report, all of which are
/// scoped to the fourteen synced leagues; adding cup and lower-division games
/// there would silently change what the model is trained and judged on. Here
/// they are read by one query — the head-to-head panel — and nothing else.
/// </remarks>
public class HeadToHeadMeeting
{
    public int Id { get; set; }

    /// <summary>The provider's fixture id, so a re-fetch updates rather than duplicates.</summary>
    public int ApiFixtureId { get; set; }

    /// <summary>Our team ids, in the roles they held in THAT match.</summary>
    public int HomeTeamId { get; set; }

    public int AwayTeamId { get; set; }

    public DateTimeOffset Date { get; set; }

    public int HomeGoals { get; set; }

    public int AwayGoals { get; set; }

    /// <summary>The provider's league id for the competition it was played in.</summary>
    public int? LeagueId { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
