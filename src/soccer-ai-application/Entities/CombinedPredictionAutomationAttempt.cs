namespace SoccerAi.Application.Entities;

public sealed class CombinedPredictionAutomationAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int FixtureId { get; set; }
    public DateTimeOffset KickoffUtc { get; set; }
    public string Window { get; set; } = "";
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public string Status { get; set; } = "running";
    public Guid? SnapshotId { get; set; }
    public string? Error { get; set; }
}
