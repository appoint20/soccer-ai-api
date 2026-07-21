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
