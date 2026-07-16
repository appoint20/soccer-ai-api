namespace SoccerAi.Application.Interfaces;

public enum LeagueTier
{
    /// <summary>Focus league — full pipeline by default.</summary>
    Tier1,

    /// <summary>Kept, not focus (European cups) — behind a flag, stricter gates.</summary>
    Tier2,

    /// <summary>Not configured — never in scope.</summary>
    Unknown
}

/// <summary>
/// Single source of truth for league scope: which leagues sync, precompute and
/// backtest operate on, and how strict qualification is per tier.
/// </summary>
public interface ILeagueTierService
{
    LeagueTier GetTier(int leagueId);

    /// <summary>Tier1 always; Tier2 only when IncludeTier2 is enabled.</summary>
    bool IsInScope(int leagueId);

    /// <summary>League ids the sync pipeline should process.</summary>
    IReadOnlyList<int> GetSyncLeagueIds();

    /// <summary>All configured Tier2 (European cup) league ids, regardless of the IncludeTier2 flag.</summary>
    IReadOnlyList<int> GetTier2LeagueIds();

    /// <summary>Extra qualification-score threshold for this league (0 for Tier1).</summary>
    double GetQualificationThresholdBoost(int leagueId);
}
