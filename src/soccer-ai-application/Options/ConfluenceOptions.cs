namespace SoccerAi.Application.Options;

/// <summary>
/// Confluence decision engine configuration ("Confluence" section).
/// Qualified = probability ≥ market threshold AND ≥ MinConfirmations confirm
/// rules fired AND zero veto rules fired. Conservative by design: better to
/// qualify 5 picks/day at 65% than 30 at 52%.
/// </summary>
public sealed class ConfluenceOptions
{
    public const string SectionName = "Confluence";

    /// <summary>K — minimum confirm rules that must fire.</summary>
    public int MinConfirmations { get; set; } = 2;

    // ── Per-market probability FLOORS (v3: EV decides value, the floor only
    //    keeps coin-flip probabilities out regardless of price) ──
    public double BttsMinProbability { get; set; } = 0.50;
    public double Over25MinProbability { get; set; } = 0.50;
    public double Goals23MinProbability { get; set; } = 0.50;
    public double WinnerMinProbability { get; set; } = 0.50;
    public double Under25MinProbability { get; set; } = 0.50;
    public double DrawMinProbability { get; set; } = 0.30; // draws rarely exceed ~35%

    // ── Per-market minimum edge: EV = p×odds − 1 must reach this ──
    public double BttsMinEdge { get; set; } = 0.05;
    public double Over25MinEdge { get; set; } = 0.05;
    public double Goals23MinEdge { get; set; } = 0.05;
    public double WinnerMinEdge { get; set; } = 0.05;
    public double Under25MinEdge { get; set; } = 0.05;
    public double DrawMinEdge { get; set; } = 0.05;

    /// <summary>Fraction of full Kelly used for reported stakes (quarter Kelly).</summary>
    public double KellyFraction { get; set; } = 0.25;

    /// <summary>
    /// Maximum legs per combo ticket. Baseline v5 measured 3-leg tickets at a
    /// 16% hit rate and −22.7% Kelly ROI vs 2-leg at 51.5% / +73.4%, so the
    /// default caps combos at 2 legs.
    /// </summary>
    public int MaxComboLegs { get; set; } = 2;

    /// <summary>
    /// Focus markets: tickets containing one are selected before any other
    /// combo and hold guaranteed daily slots.
    ///
    /// Under 2.5 joined BTTS and Over 2.5 after baseline v11, where it was the
    /// strongest market measured (n=31, 58.1% hit, +21.0% flat) while Over 2.5
    /// lost money (n=47, 48.9%, −4.7%). Note that .NET configuration binding
    /// appends to array defaults, so an override here adds to this list.
    /// </summary>
    public string[] GoalsMarkets { get; set; } = ["btts", "over25", "under25"];

    /// <summary>
    /// Guaranteed daily slots for tickets containing a focus market.
    /// </summary>
    public int MinGoalsMarketTickets { get; set; } = 3;

    /// <summary>
    /// Legs a single combo may take from the same league. 0 means no limit.
    ///
    /// Previously fixed at one. With thirteen leagues and a strict edge bar,
    /// that discarded most of the day's best combinations — on a weekend where
    /// the three strongest picks were all Championship matches it produced no
    /// ticket at all.
    ///
    /// The cost of lifting it is worth stating: ticket probability is the
    /// product of its legs, which assumes independence. Two fixtures in the
    /// same league on the same day share weather, referees and fixture
    /// congestion, so they fail together more often than that product implies,
    /// and such tickets are priced slightly optimistically. The backtest's
    /// ticket section is where that would show up.
    /// </summary>
    public int MaxLegsPerLeague { get; set; }

    /// <summary>
    /// Combo legs must clear the SAME bar as a single pick (EV ≥ MinEdge), not
    /// merely EV &gt; 0. Baseline v7: 2-leg tickets built from the weaker pool
    /// hit 16.7% against a claimed 27% — probability error multiplies in a
    /// parlay, so each leg needs more confidence, not less.
    /// </summary>
    public bool ComboLegsRequireQualified { get; set; } = true;

    /// <summary>
    /// Whether to compose combos from legs that have no bookmaker price.
    /// </summary>
    /// <remarks>
    /// The value gate rejects an unpriced market before it looks at anything
    /// else, so with no odds feed the combo board is empty however good the
    /// model's probabilities are. With this on, legs that pass every non-price
    /// check are combined and published as analysis: probability and fair odds
    /// are real, while total odds, EV and Kelly stake are null because no quote
    /// exists to compute them from.
    ///
    /// These tickets are never recorded in the ledger and never reach the
    /// performance figures — an unpriced suggestion has no return to measure.
    /// </remarks>
    public bool AllowUnpricedCombos { get; set; } = true;

    /// <summary>
    /// Minimum combined probability for an unpriced combo to be published.
    /// </summary>
    /// <remarks>
    /// The odds floor normally stops implausible accumulators; without prices
    /// this is the only thing standing between the board and a three-leg parlay
    /// nobody should look at.
    /// </remarks>
    public double UnpricedComboMinProbability { get; set; } = 0.20;

    // ── Product 2: confidence picks (predictions, NOT value bets) ──

    /// <summary>Top-N matches per day ranked by calibrated probability.</summary>
    public int ConfidencePicksPerDay { get; set; } = 5;

    /// <summary>Minimum calibrated probability to appear as a confidence pick.</summary>
    public double ConfidencePickMinProbability { get; set; } = 0.60;

    /// <summary>
    /// Per-market overrides of <see cref="ConfidencePickMinProbability"/>.
    ///
    /// Over 2.5 sits at 0.65 because baseline v9 measured a specific failure:
    /// across all fixtures, Over 2.5 in the 60-65% band hit 66.7% (n=123) — but
    /// the subset *selected as the best market on its fixture* hit only 48.8%
    /// (n=41). Choosing the maximum does not merely inflate the number; it
    /// picks fixtures where every other market looked weak, and those are
    /// systematically different matches.
    ///
    /// Treat this as provisional: n=41 is thin, and the honest fix is to publish
    /// measured bucket hit rates rather than model probabilities. Raise or
    /// remove the entry in appsettings once more data arrives.
    ///
    /// Note that .NET configuration merges dictionary entries by key rather than
    /// replacing the dictionary, so an override here adds to these defaults.
    /// </summary>
    public Dictionary<string, double> ConfidencePickMinProbabilityByMarket { get; set; } = new()
    {
        ["over25"] = 0.65
    };

    /// <summary>MinEdge levels reported side by side in the backtest EV sweep.</summary>
    public double[] EvSweepLevels { get; set; } = [0.02, 0.03, 0.04, 0.05, 0.07];

    /// <summary>
    /// Backtest ROI sections only include fixture weeks whose odds coverage
    /// reaches this share — low-coverage (blackout) weeks distort ROI.
    /// Brier/calibration always use all fixtures.
    /// </summary>
    public double RoiMinWeeklyOddsCoverage { get; set; } = 0.60;

    /// <summary>Extra probability demanded for Tier2 (cup) fixtures.</summary>
    public double Tier2ExtraProbability { get; set; } = 0.05;

    /// <summary>
    /// Markets that are permanently analysis-only: API-Football offers no odds
    /// for them, so they can never be priced picks. Full analysis still runs.
    /// </summary>
    public string[] InformationalOnlyMarkets { get; set; } = ["goals_2_3"];

    // ── Shadow cohort: named winner-band hypothesis ──
    public double ShadowWinnerMinProbability { get; set; } = 0.62;
    public double ShadowWinnerOddsMin { get; set; } = 1.40;
    public double ShadowWinnerOddsMax { get; set; } = 2.10;

    // ── Draw confirm-rule thresholds ──
    public double DrawPpgGapMax { get; set; } = 0.30;
    public double DrawH2HRateConfirm { get; set; } = 0.40;
    public double DrawLowScoringAvgGoals { get; set; } = 2.5;

    // ── Rule thresholds ──
    public int ScoredInVenueConfirmCount { get; set; } = 2;    // of last 3 venue matches
    public int ConcededInVenueConfirmCount { get; set; } = 2;  // of last 3 venue matches
    public double H2HBttsRateConfirm { get; set; } = 0.60;
    public double H2HOver25RateConfirm { get; set; } = 0.60;
    public double H2HOverAvgGoalsConfirm { get; set; } = 3.0;
    public double H2HQuietAvgGoals { get; set; } = 2.0;
    public int MinH2HSample { get; set; } = 3;
    public int LeakyDefenseConcededCount { get; set; } = 4;    // of last 5 venue matches
    public double Goals23H2HBandLow { get; set; } = 2.0;
    public double Goals23H2HBandHigh { get; set; } = 3.0;
    public double ChaosVetoAvgGoals { get; set; } = 3.5;
    public double WinnerVenuePpgConfirm { get; set; } = 2.0;

    // ── Decision tier mapping ──
    public int StrongBetExtraConfirms { get; set; } = 1;       // K + this → StrongBet
    public double StrongBetExtraProbability { get; set; } = 0.08;
}
