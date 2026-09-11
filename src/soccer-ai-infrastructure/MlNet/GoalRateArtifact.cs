using System.Security.Cryptography;
using System.Text.Json;
using SoccerAi.Application.Options;
using SoccerAi.Infrastructure.MlNet.Models;

namespace SoccerAi.Infrastructure.MlNet;

/// <summary>One immutable model pair, calibration and feature contract.</summary>
public sealed record GoalRateArtifact
{
    public const string PointerFile = "goal-rate-current.json";
    public string SchemaVersion { get; init; } = GoalRateFeatureBuilder.SchemaVersion;
    public string Generation { get; init; } = "";
    public string PredictionRecipe { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTime TrainingThroughUtc { get; init; }
    public DateTime CalibrationFromUtc { get; init; }
    public DateTime CalibrationThroughUtc { get; init; }
    public string Trainer { get; init; } = "";
    public string[] Features { get; init; } = [];
    public DixonColesOptions DixonColes { get; init; } = new();
    public double LambdaMin { get; init; }
    public double LambdaMax { get; init; }
    public Dictionary<string, string> Sha256 { get; init; } = [];

    public static string Hash(string file) => Convert.ToHexString(
        SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();

    public bool Supports(DixonColesOptions dc, HybridModelOptions hybrid) =>
        SchemaVersion == GoalRateFeatureBuilder.SchemaVersion &&
        // Empty recipe is the preceding, versioned pure-ML generation. Keep it
        // usable while a new candidate is evaluated; unknown recipes fail closed.
        (PredictionRecipe == "" || PredictionRecipe == GoalRateEnsemble.Recipe) &&
        Features.SequenceEqual(GoalRateRow.FeatureColumns()) &&
        JsonSerializer.Serialize(DixonColes) == JsonSerializer.Serialize(dc) &&
        LambdaMin == hybrid.LambdaMin && LambdaMax == hybrid.LambdaMax &&
        TrainingThroughUtc.Date < CalibrationFromUtc.Date &&
        CalibrationFromUtc <= CalibrationThroughUtc;

    public bool CanScore(DateTime kickoff) => kickoff.Date > CalibrationThroughUtc.Date;
}

public sealed record GoalRateGenerationPointer(string Generation);
