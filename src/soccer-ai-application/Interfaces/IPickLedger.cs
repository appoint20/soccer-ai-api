namespace SoccerAi.Application.Interfaces;

/// <summary>Realized performance for one slice of the ledger.</summary>
/// <param name="Settled">Tickets with a final result (void excluded).</param>
/// <param name="Key">Slice identity: "overall", a ticket kind, or a market.</param>
/// <param name="Won">Settled tickets that returned a profit.</param>
/// <param name="Pending">Published but not yet settleable.</param>
/// <param name="Voided">Excluded from ROI — abandoned or unsettleable fixtures.</param>
/// <param name="Staked">Total staked at one flat unit per ticket.</param>
/// <param name="Returned">Total returned on winning tickets.</param>
public sealed record PickPerformanceSlice(
    string Key,
    int Settled,
    int Won,
    int Pending,
    int Voided,
    double Staked,
    double Returned)
{
    public double HitRate => Settled > 0 ? (double)Won / Settled : 0;

    /// <summary>Flat-stake return on investment. Zero settled tickets means zero, not a claim.</summary>
    public double Roi => Staked > 0 ? (Returned - Staked) / Staked : 0;
}

/// <summary>One ISO week of realized results.</summary>
/// <param name="Label">ISO week, e.g. "W31".</param>
/// <param name="Settled">Settled tickets that week — lets a caller suppress a week too thin to publish.</param>
/// <param name="ProfitUnits">
/// Signed profit in units of one flat stake, never a currency amount, so the
/// figure means the same thing whatever the reader stakes.
/// </param>
public sealed record PickWeeklySlice(string Label, int Settled, double ProfitUnits);

/// <summary>Realized results overall, by ticket kind, by market and by week.</summary>
public sealed record PickPerformance(
    DateOnly From,
    DateOnly To,
    PickPerformanceSlice Overall,
    IReadOnlyList<PickPerformanceSlice> ByKind,
    IReadOnlyList<PickPerformanceSlice> ByMarket,
    IReadOnlyList<PickWeeklySlice> ByWeek);

/// <summary>
/// The record of what was actually published and how it finished.
///
/// A backtest says what a strategy would have returned. Only this ledger says
/// what it did return, at the prices customers were shown. Everything the
/// product claims about performance should ultimately be traceable to here.
/// </summary>
public interface IPickLedger
{
    /// <summary>
    /// Records a published board. Idempotent: republishing the same board
    /// changes nothing, and prices already recorded are never overwritten.
    /// </summary>
    /// <returns>Number of tickets newly recorded.</returns>
    Task<int> RecordAsync(DailyPickBoard board, CancellationToken ct = default);

    /// <summary>
    /// Settles every pending ticket whose fixtures have finished.
    /// </summary>
    /// <returns>Number of tickets moved out of pending.</returns>
    Task<int> SettleAsync(CancellationToken ct = default);

    Task<PickPerformance> GetPerformanceAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
