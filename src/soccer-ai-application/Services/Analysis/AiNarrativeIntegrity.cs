using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Services.Analysis;

public static class AiNarrativeIntegrity
{
    public static bool NeedsSnapshotRefresh(MatchAnalysis? snapshot, FixtureAnalysis? row) =>
        row is not null && !string.IsNullOrWhiteSpace(row.Analysis) &&
        (snapshot?.Ai is not { } ai || ai.Analysis != row.Analysis || ai.GeneratedAtUtc != row.AiGeneratedAtUtc);

    public static string? InvalidResult(AiBilingualResult? result) => result switch
    {
        null => "missing result",
        { Confidence: < 0 or > 100 } or { OverallConfidence: < 0 or > 100 } => "confidence outside 0–100",
        { Over25Qualified: true, Under25Qualified: true } => "contradictory Over/Under decisions",
        { HomeWinQualified: true, AwayWinQualified: true } => "contradictory winner decisions",
        _ when string.IsNullOrWhiteSpace(result.En?.Analysis) || string.IsNullOrWhiteSpace(result.De?.Analysis) => "missing English or German narrative",
        _ => null
    };
}
