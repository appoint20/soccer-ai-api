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
    /// How far back the routine sync will spend API calls looking for odds.
    ///
    /// This is a quota guard, not a data policy: re-checking every historical
    /// fixture on every sync would cost thousands of calls a day. The gap it
    /// leaves is what <c>backfill-odds</c> exists to close.
    /// </summary>
    public int LookbackDays { get; set; } = 7;

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
