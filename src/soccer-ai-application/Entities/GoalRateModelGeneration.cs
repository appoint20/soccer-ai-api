namespace SoccerAi.Application.Entities;

/// <summary>An accepted immutable model bundle shared by the worker and API.</summary>
public sealed class GoalRateModelGeneration
{
    public string Generation { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string ManifestJson { get; set; } = "";
    public string CalibrationJson { get; set; } = "";
    public string EvaluationJson { get; set; } = "";
    public byte[] HomeModel { get; set; } = [];
    public byte[] AwayModel { get; set; } = [];
}
