namespace soccer_gpt_application.Models;

/// <summary>
/// API response models for free-api-live-football-data.p.rapidapi.com
/// </summary>
public record EuropeanFixturesApiResponse
{
    public string Status { get; init; } = string.Empty;
    public EuropeanFixturesResponseData? Response { get; init; }
}

public record EuropeanFixturesResponseData
{
    public List<EuropeanMatch> Matches { get; init; } = new();
}

public record EuropeanMatch
{
    public string Id { get; init; } = string.Empty;
    public string PageUrl { get; init; } = string.Empty;
    public EuropeanTeam Home { get; init; } = new();
    public EuropeanTeam Away { get; init; } = new();
    public EuropeanMatchStatus Status { get; init; } = new();
    public bool DisplayTournament { get; init; }
    public bool NotStarted { get; init; }
}

public record EuropeanTeam
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Score { get; init; }
}

public record EuropeanMatchStatus
{
    public string UtcTime { get; init; } = string.Empty;
    public bool Finished { get; init; }
    public bool Started { get; init; }
    public bool Cancelled { get; init; }
    public bool Awarded { get; init; }
    public string ScoreStr { get; init; } = string.Empty;
    public EuropeanMatchReason? Reason { get; init; }
}

public record EuropeanMatchReason
{
    public string Short { get; init; } = string.Empty;
    public string ShortKey { get; init; } = string.Empty;
    public string Long { get; init; } = string.Empty;
    public string LongKey { get; init; } = string.Empty;
}

/// <summary>
/// Processed fixture data for team-based lookup
/// </summary>
public record TeamEuropeanFixtures
{
    public string TeamName { get; init; } = string.Empty;
    public List<ProcessedFixture> UclFixtures { get; init; } = new();
    public List<ProcessedFixture> UelFixtures { get; init; } = new();
    public int TotalEuropeanFixtures { get; init; }
    public List<ProcessedFixture> RecentMatches { get; init; } = new();
    public List<ProcessedFixture> UpcomingMatches { get; init; } = new();
    public bool HasRecentEuropean { get; init; }
    public bool HasUpcomingEuropean { get; init; }
}

public record ProcessedFixture
{
    public string MatchId { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public DateTime? DateParsed { get; init; }
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public int HomeScore { get; init; }
    public int AwayScore { get; init; }
    public string Competition { get; init; } = string.Empty;
    public bool Finished { get; init; }
    public bool Started { get; init; }
    public string Venue { get; init; } = string.Empty; // "home" or "away"
}

public record EuropeanCompetition
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ShortCode { get; init; } = string.Empty;
}
