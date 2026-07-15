using System.Text.Json;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Services.Analysis;

/// <summary>
/// Serializes precomputed MatchAnalysis responses into FixtureAnalysis.SnapshotJson.
/// One fixed options instance so write and read are always symmetric.
/// </summary>
public static class AnalysisSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(MatchAnalysis analysis) =>
        JsonSerializer.Serialize(analysis, Options);

    public static MatchAnalysis? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<MatchAnalysis>(json, Options);
        }
        catch (JsonException)
        {
            return null; // corrupt/legacy snapshot → caller recomputes
        }
    }
}
