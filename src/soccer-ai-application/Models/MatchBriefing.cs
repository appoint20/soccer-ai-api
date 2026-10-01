namespace SoccerAi.Application.Models;

public sealed record BriefingEvidence(string Text, string Direction);
public sealed record BriefingOutcome(string Outcome, double? Probability);
public sealed record BriefingGoalProfile(string Market, double? Probability, IReadOnlyList<BriefingEvidence> Evidence);
public sealed record BriefingSource(string Name, string Status, DateTimeOffset? CapturedAtUtc);

public sealed record MatchBriefing
{
    public string Version { get; init; } = "match-analysis-v1";
    public int Id { get; init; }
    public DateTimeOffset Date { get; init; }
    public string Status { get; init; } = "NS";
    public string League { get; init; } = "";
    public string HomeTeam { get; init; } = "";
    public string AwayTeam { get; init; } = "";
    public string Language { get; init; } = "en";
    public string DataStatus { get; init; } = "limited";
    public DateTimeOffset? AnalysisUpdatedAtUtc { get; init; }
    public int? HomeGoals { get; init; }
    public int? AwayGoals { get; init; }
    public int? ElapsedMinutes { get; init; }
    public IReadOnlyList<string> SummaryLines { get; init; } = [];
    public bool AiGenerated { get; init; }
    public IReadOnlyList<string> Limitations { get; init; } = [];
    public IReadOnlyList<BriefingOutcome> Outcomes { get; init; } = [];
    public IReadOnlyList<BriefingGoalProfile> GoalProfiles { get; init; } = [];
    public IReadOnlyList<BriefingSource> Sources { get; init; } = [];
    public TeamStats HomeStats { get; init; } = TeamStats.Empty;
    public TeamStats AwayStats { get; init; } = TeamStats.Empty;
    [System.Text.Json.Serialization.JsonPropertyName("h2h")]
    public HeadToHeadModel? H2H { get; init; }
}

public sealed record BriefingHistoryEntry(DateTimeOffset CapturedAtUtc, DateTimeOffset KickoffUtc,
    string ModelVersion, IReadOnlyList<BriefingOutcome> Outcomes, IReadOnlyList<BriefingOutcome> GoalProfiles);
