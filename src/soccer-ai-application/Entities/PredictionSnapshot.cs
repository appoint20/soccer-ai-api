namespace SoccerAi.Application.Entities;

/// <summary>Immutable pre-match probabilities. Mutable analysis caches are never a scoring ledger.</summary>
public sealed class PredictionSnapshot
{
    public int Id { get; set; }
    public int FixtureId { get; set; }
    public DateTimeOffset CapturedAtUtc { get; set; }
    public long CaptureWindow { get; set; }
    public DateTimeOffset KickoffUtc { get; set; }
    public string ModelVersion { get; set; } = "unknown";
    public string ContextJson { get; set; } = "{}";
    public string Winner { get; set; } = "";
    public bool Over25Pick { get; set; }
    public bool BttsPick { get; set; }
    public bool Goals23Pick { get; set; }
    public double Home { get; set; }
    public double Draw { get; set; }
    public double Away { get; set; }
    public double Over25 { get; set; }
    public double Btts { get; set; }
    public double Goals23 { get; set; }
    public double RawHome { get; set; }
    public double RawDraw { get; set; }
    public double RawAway { get; set; }
    public double RawOver25 { get; set; }
    public double RawBtts { get; set; }
    public double RawGoals23 { get; set; }
}
