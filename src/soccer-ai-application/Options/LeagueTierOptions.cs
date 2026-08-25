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
    /// England: 39 PL, 40 Championship, 41 League One, 42 League Two;
    /// Germany: 78 Bundesliga, 79 2. Bundesliga, 80 3. Liga;
    /// Spain: 140 La Liga, 141 La Liga 2; Italy: 135 Serie A, 136 Serie B;
    /// France: 61 Ligue 1, 62 Ligue 2.
    /// </summary>
    /// <remarks>
    /// Ids 46 and 5 used to be listed here as "National League" and a "legacy
    /// placeholder" for it. Neither has ever produced a fixture: every backtest
    /// baseline in this repo covers exactly the thirteen leagues below and no
    /// National League at all, while both ids were still costing a sync call
    /// per run. They are removed until the real id is confirmed against the
    /// provider — one call settles it:
    ///
    ///   GET https://v3.football.api-sports.io/leagues?country=England
    ///
    /// Add the confirmed id back through configuration; note the binder
    /// APPENDS to this default rather than replacing it (see
    /// <see cref="Services.LeagueTierService.GetSyncLeagueIds"/>), and give it
    /// a name in <see cref="Services.LeagueCatalog"/> or the board will label
    /// it "League {id}".
    /// </remarks>
    public int[] Tier1 { get; set; } =
        [39, 40, 41, 42, 78, 79, 80, 140, 141, 135, 136, 61, 62];

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
