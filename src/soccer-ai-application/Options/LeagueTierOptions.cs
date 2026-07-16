namespace SoccerAi.Application.Options;

/// <summary>
/// League tier configuration ("LeagueTiers" section).
///
/// Tier1 = focus leagues: sync, precompute and backtest include them by default.
/// Tier2 = kept but not focus (European cups): included only when
/// <see cref="IncludeTier2"/> is true, and qualification is stricter there.
/// API-Football league ids.
/// </summary>
public sealed class LeagueTierOptions
{
    public const string SectionName = "LeagueTiers";

    /// <summary>
    /// England: 39 PL, 40 Championship, 41 League One, 42 League Two,
    /// 46 National League (5 = legacy dynamic placeholder for it);
    /// Germany: 78 Bundesliga, 79 2. Bundesliga, 80 3. Liga;
    /// Spain: 140 La Liga, 141 La Liga 2; Italy: 135 Serie A, 136 Serie B;
    /// France: 61 Ligue 1, 62 Ligue 2.
    /// </summary>
    public int[] Tier1 { get; set; } =
        [39, 40, 41, 42, 46, 5, 78, 79, 80, 140, 141, 135, 136, 61, 62];

    /// <summary>2 Champions League, 3 Europa League, 848 Conference League.</summary>
    public int[] Tier2 { get; set; } = [2, 3, 848];

    /// <summary>Include Tier2 leagues in sync/precompute/backtest. Default off.</summary>
    public bool IncludeTier2 { get; set; }

    /// <summary>
    /// Extra points added to qualification score thresholds for Tier2 fixtures
    /// (cup matches are noisier — demand stronger evidence).
    /// </summary>
    public double Tier2QualificationThresholdBoost { get; set; } = 10.0;
}
