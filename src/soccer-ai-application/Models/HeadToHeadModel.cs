namespace SoccerAi.Application.Models;

/// <summary>
/// Head-to-head analysis - facts and rates only, no decisions
/// </summary>
/// <summary>
/// Past meetings between the two teams in the current fixture.
///
/// ORIENTATION: every rate is oriented to the CURRENT fixture's home and away
/// team, not to who happened to be at home in each historical meeting. For each
/// past match the goals are re-attributed to the current home/away teams before
/// counting, so <see cref="HomeWinRate"/> is "how often the side playing at home
/// today beat this opponent", wherever that match was played. A client can
/// render these directly against today's teams without inverting anything.
/// </summary>
public sealed class HeadToHeadModel
{
    public int MatchesAnalyzed { get; init; }
    
    // Rates (normalized 0-1)
    /// <summary>Share of meetings drawn. Range 0-1.</summary>
    public double DrawRate { get; init; }
    /// <summary>Share of meetings won by the CURRENT fixture's home team. Range 0-1.</summary>
    public double HomeWinRate { get; init; }
    /// <summary>Share of meetings won by the CURRENT fixture's away team. Range 0-1.</summary>
    public double AwayWinRate { get; init; }
    public double BTTSRate { get; init; }
    public double Over25Rate { get; init; }
    public double TwoToThreeGoalsRate { get; init; }
    
    // Averages
    public double AvgGoalsHome { get; init; }
    public double AvgGoalsAway { get; init; }
    public double AvgTotalGoals { get; init; }
    
    public DateTimeOffset? LastMatchDate { get; init; }
    
    /// <summary>
    /// True once there are at least 3 meetings — below that the rates swing on
    /// a single result and mean little. This is the sufficiency rule the server
    /// applies; a client should gate its head-to-head display on this rather
    /// than re-deriving its own threshold, so the two cannot drift apart.
    /// </summary>
    public bool IsValid => MatchesAnalyzed >= 3;
    
    public static HeadToHeadModel Empty => new() { MatchesAnalyzed = 0 };
}
