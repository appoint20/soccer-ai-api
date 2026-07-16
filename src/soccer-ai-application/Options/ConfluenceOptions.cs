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

    // ── Per-market probability thresholds (calibrated DC probability) ──
    public double BttsMinProbability { get; set; } = 0.55;
    public double Over25MinProbability { get; set; } = 0.55;
    public double Goals23MinProbability { get; set; } = 0.45;
    public double WinnerMinProbability { get; set; } = 0.55;
    public double Under25MinProbability { get; set; } = 0.55;

    /// <summary>Extra probability demanded for Tier2 (cup) fixtures.</summary>
    public double Tier2ExtraProbability { get; set; } = 0.05;

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
