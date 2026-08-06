namespace SoccerAi.Application.Options;

/// <summary>
/// Odds acquisition policy.
///
/// The important thing to understand about odds in this system: they are
/// <em>captured</em>, not derived. A fixture's price exists in the database only
/// because a sync ran while that price was still being offered. If the worker is
/// down for a week, that week has no odds — and since no probability can be
/// checked for value without a price, those fixtures are permanently invisible
/// to the value gate unless they are explicitly backfilled.
/// </summary>
public sealed class OddsSyncOptions
{
    public const string SectionName = "OddsSync";

    /// <summary>
    /// API-Football retains only the last seven days of pre-match odds. Older
    /// fixtures return an empty odds payload no matter how they are requested,
    /// which a probe of twenty historical fixtures confirmed: nought priced.
    ///
    /// So this is a hard limit of the data source, not a tuning knob. Raising it
    /// buys nothing but wasted calls.
    ///
    /// The consequence is the single most important operational fact in this
    /// project: <b>odds coverage equals worker uptime</b>. A day the worker does
    /// not run is a day whose prices are gone permanently, and unpriced fixtures
    /// are invisible to the value gate forever after.
    /// </summary>
    public int LookbackDays { get; set; } = 7;

    /// <summary>
    /// Days of pre-match odds history the provider serves. Used to stop the
    /// backfill from spending calls on fixtures the API cannot answer for.
    /// </summary>
    public int ApiOddsRetentionDays { get; set; } = 7;

    /// <summary>Hard ceiling on API calls one backfill run may spend.</summary>
    public int BackfillMaxCalls { get; set; } = 500;

    /// <summary>
    /// Fixtures sampled before committing to a full backfill. API-Football does
    /// not serve odds for arbitrarily old fixtures, and the cutoff varies by
    /// plan — so the sample answers the question instead of assuming it.
    /// </summary>
    public int BackfillProbeSize { get; set; } = 20;

    /// <summary>
    /// Abort the backfill when the probe finds prices for fewer than this share
    /// of sampled fixtures. Spending hundreds of calls on a window the API no
    /// longer prices is the failure mode worth preventing.
    /// </summary>
    public double BackfillMinProbeHitRate { get; set; } = 0.20;
}
