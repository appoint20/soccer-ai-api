namespace SoccerAi.Application.Options;

/// <summary>
/// All strategic-signal windows and flag thresholds ("Strategy" config section).
/// Signals gate decisions — they NEVER modify probabilities.
/// </summary>
public sealed class StrategyOptions
{
    public const string SectionName = "Strategy";

    // ── Windows ──
    public int ShortWindow { get; set; } = 3;
    public int MidWindow { get; set; } = 5;
    public int LongWindow { get; set; } = 10;
    public int H2HWindow { get; set; } = 5;
    public int H2HLongWindow { get; set; } = 10;

    // ── A. Scoring pattern flags ──
    public int FailedToScoreFlagCount { get; set; } = 2;
    public int CleanSheetFlagCount { get; set; } = 2;
    public double HighRateFlag { get; set; } = 0.60;           // O2.5 / BTTS rates
    public double GoalTrendFlagDelta { get; set; } = 0.30;     // last5 vs season avg
    public double ChaosAvgGoalsFlag { get; set; } = 3.0;
    public double SecondHalfShareFlag { get; set; } = 0.65;
    public int ScoringDroughtFlagLength { get; set; } = 2;

    // ── B. Form flags ──
    public double FormDeltaFlag { get; set; } = 0.50;          // PPG last5 vs season
    public int StreakFlagLength { get; set; } = 3;
    public double TightGameShareFlag { get; set; } = 0.60;     // 1-goal margins share

    // ── C. Table context flags ──
    public int RankGapFlag { get; set; } = 6;
    public double PpgGapFlag { get; set; } = 0.50;
    public int OpeningPhaseMatchdays { get; set; } = 5;
    public int RunInMatchdays { get; set; } = 6;

    // ── D. H2H flags ──
    public double H2HHighRateFlag { get; set; } = 0.60;
    public int DominanceUnbeatenCount { get; set; } = 4;       // of last 5 H2H
    public double StyleClashGoalDiff { get; set; } = 1.0;

    // ── E. Schedule flags ──
    public int RestDayGapFlag { get; set; } = 3;
    public int CongestionMatchesIn14Days { get; set; } = 4;
    public int Tier2ProximityDays { get; set; } = 4;

    // ── Minimum odds floors (below floor = "analysis only", never a pick) ──
    public double MinOddsBtts { get; set; } = 1.70;
    public double MinOddsOver25 { get; set; } = 1.70;
    public double MinOddsUnder25 { get; set; } = 1.70;
    public double MinOddsGoals23 { get; set; } = 1.70;
    public double MinOdds1X2 { get; set; } = 2.10;

    /// <summary>
    /// Minimum price for a same-match BTTS+Over2.5 pair. Higher than the plain
    /// goals floor because the pair exists to rescue "sure" matches priced
    /// below 1.70 — it must clear a worthwhile price to be sellable.
    /// </summary>
    public double MinOddsSameMatchPair { get; set; } = 1.85;

    // ── G. Market flags ──
    public double ModelMarketDivergenceFlag { get; set; } = 0.15;

    /// <summary>|opening→latest| relative price move that flags the drift signal.</summary>
    public double OpeningDriftFlagPct { get; set; } = 0.10;
    public double HeavyFavoriteOdds { get; set; } = 1.40;
    public double ModerateFavoriteOdds { get; set; } = 2.00;
    public double BalancedFavoriteOdds { get; set; } = 2.75;
    public int TrapRankGap { get; set; } = 5;                  // market favors much worse-ranked side

    // ── H. League profile flags ──
    public double LeagueDeviationFlag { get; set; } = 0.15;
    public double HighVolatilityFlag { get; set; } = 0.20;

    /// <summary>Zone layout per API-Football league id; Default when absent.</summary>
    public Dictionary<int, LeagueZoneProfile> LeagueProfiles { get; set; } = DefaultProfiles();

    public LeagueZoneProfile DefaultProfile { get; set; } = new();

    public LeagueZoneProfile GetProfile(int leagueId) =>
        LeagueProfiles.GetValueOrDefault(leagueId, DefaultProfile);

    private static Dictionary<int, LeagueZoneProfile> DefaultProfiles() => new()
    {
        [39] = new LeagueZoneProfile { LeagueSize = 20, RelegationSpots = 3, EuropeanSpots = 5 },                        // Premier League
        [40] = new LeagueZoneProfile { LeagueSize = 24, RelegationSpots = 3, PlayoffStart = 3, PlayoffEnd = 6 },         // Championship
        [41] = new LeagueZoneProfile { LeagueSize = 24, RelegationSpots = 4, PlayoffStart = 3, PlayoffEnd = 6 },         // League One
        [42] = new LeagueZoneProfile { LeagueSize = 24, RelegationSpots = 2, PlayoffStart = 4, PlayoffEnd = 7 },         // League Two
        [46] = new LeagueZoneProfile { LeagueSize = 24, RelegationSpots = 4, PlayoffStart = 2, PlayoffEnd = 7 },         // National League
        [78] = new LeagueZoneProfile { LeagueSize = 18, RelegationSpots = 3, EuropeanSpots = 6 },                        // Bundesliga
        [79] = new LeagueZoneProfile { LeagueSize = 18, RelegationSpots = 3, PlayoffStart = 3, PlayoffEnd = 3 },         // 2. Bundesliga
        [80] = new LeagueZoneProfile { LeagueSize = 20, RelegationSpots = 4, PlayoffStart = 3, PlayoffEnd = 3 },         // 3. Liga
        [140] = new LeagueZoneProfile { LeagueSize = 20, RelegationSpots = 3, EuropeanSpots = 6 },                       // La Liga
        [141] = new LeagueZoneProfile { LeagueSize = 22, RelegationSpots = 4, PlayoffStart = 3, PlayoffEnd = 6 },        // La Liga 2
        [135] = new LeagueZoneProfile { LeagueSize = 20, RelegationSpots = 3, EuropeanSpots = 6 },                       // Serie A
        [136] = new LeagueZoneProfile { LeagueSize = 20, RelegationSpots = 3, PlayoffStart = 3, PlayoffEnd = 8 },        // Serie B
        [61] = new LeagueZoneProfile { LeagueSize = 18, RelegationSpots = 3, EuropeanSpots = 5 },                        // Ligue 1
        [62] = new LeagueZoneProfile { LeagueSize = 18, RelegationSpots = 3, PlayoffStart = 3, PlayoffEnd = 5 }          // Ligue 2
    };
}

public sealed class LeagueZoneProfile
{
    public int LeagueSize { get; set; } = 20;
    public int TitleSpots { get; set; } = 3;
    public int EuropeanSpots { get; set; }
    /// <summary>Promotion playoff rank range (0 = league has none).</summary>
    public int PlayoffStart { get; set; }
    public int PlayoffEnd { get; set; }
    public int RelegationSpots { get; set; } = 3;

    public int TotalMatchdays => (LeagueSize - 1) * 2;
}
