namespace SoccerAi.Application.Entities;

public sealed class CombinedPredictionSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int FixtureId { get; set; }
    public DateTimeOffset KickoffUtc { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public string PredictionJson { get; set; } = "";
    public string EvidenceJson { get; set; } = "";
}
