using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;
using System.Text.RegularExpressions;

namespace SoccerAi.Application.Services.Analysis;

public static class AiNarrativeIntegrity
{
    public static int WordCount(string text) => Regex.Matches(text, @"\p{L}+(?:['’\-]\p{L}+)*").Count;

    // Boundaries require whitespace: the decimal in "Over 2.5" is not a sentence.
    public static int SentenceCount(string text) =>
        Regex.Matches(text.Trim(), @"[.!?]+(?=\s|$)").Count;

    public static bool IsCompleteSentence(string text) =>
        text.TrimEnd().EndsWith('.') || text.TrimEnd().EndsWith('!') || text.TrimEnd().EndsWith('?');

    /// <summary>Applied to newly generated provider output; legacy stored opinions remain readable.</summary>
    public static string? InvalidAnalysisLength(AiBilingualResult result)
    {
        foreach (var block in new[] { result.En, result.De })
        {
            var text = block?.Analysis ?? "";
            if (SentenceCount(text) is < 4 or > 8 || !IsCompleteSentence(text) || WordCount(text) < 60)
                return "English and German analysis each require four to eight complete sentences and at least 60 words";
        }
        return null;
    }

    public static bool NeedsSnapshotRefresh(MatchAnalysis? snapshot, FixtureAnalysis? row) =>
        row is not null && !string.IsNullOrWhiteSpace(row.Analysis) &&
        (snapshot?.Ai is not { } ai || ai.Analysis != row.Analysis || ai.GeneratedAtUtc != row.AiGeneratedAtUtc);

    public static string? InvalidResult(AiBilingualResult? result) => result switch
    {
        null => "missing result",
        { Confidence: < 0 or > 100 } or { OverallConfidence: < 0 or > 100 } => "confidence outside 0–100",
        { Over25Qualified: true, Under25Qualified: true } => "contradictory Over/Under decisions",
        { BttsAndOver25Qualified: true, Under25Qualified: true } => "contradictory combined/Under decisions",
        { HomeWinQualified: true, AwayWinQualified: true } => "contradictory winner decisions",
        _ when string.IsNullOrWhiteSpace(result.En?.Analysis) || string.IsNullOrWhiteSpace(result.De?.Analysis) => "missing English or German narrative",
        _ => null
    };
}
