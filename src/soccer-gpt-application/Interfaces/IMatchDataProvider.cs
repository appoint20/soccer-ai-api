using soccer_gpt_application.Entities;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

/// <summary>
/// Loads all match-related data (team stats, head-to-head) for a fixture.
/// Moves DB queries out of MatchAnalysisService.
/// </summary>
public interface IMatchDataProvider
{
    Task<MatchData> LoadAsync(Fixture fixture, CancellationToken ct);
}

/// <summary>
/// Container for all pre-computed match data needed by the pipeline.
/// </summary>
public sealed class MatchData
{
    public required TeamStatsResponse TeamStats { get; init; }
    public required HeadToHeadModel H2H { get; init; }
}
