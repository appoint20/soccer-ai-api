using SoccerAi.Application.Services.Decisions;

namespace SoccerAi.Application.Interfaces;

/// <summary>
/// How much of the day's board could be priced. Published alongside the picks
/// because an empty board caused by missing odds means something very different
/// to an empty board caused by the model finding no edge, and users deserve to
/// know which one they are looking at.
/// </summary>
public sealed record PickCoverage(int Fixtures, int Analyzed, int Priced)
{
    public double PricedShare => Fixtures > 0 ? (double)Priced / Fixtures : 0;
}

/// <summary>
/// One day's sellable output, assembled from the same selection code the
/// backtest measures.
/// </summary>
public sealed record DailyPickBoard(
    DateOnly Date,
    IReadOnlyList<Ticket> Tickets,
    IReadOnlyList<ConfidencePick> ConfidencePicks,
    IReadOnlyDictionary<int, FixtureRef> Fixtures,
    PickCoverage Coverage)
{
    public static DailyPickBoard Empty(DateOnly date) =>
        new(date, [], [], new Dictionary<int, FixtureRef>(), new PickCoverage(0, 0, 0));
}

/// <summary>
/// Produces the daily pick board for the live product.
///
/// Probabilities come only from the statistical pipeline; this service selects
/// and prices, it never predicts.
/// </summary>
public interface IDailyPickService
{
    Task<DailyPickBoard> GetBoardAsync(DateOnly date, string lang, CancellationToken ct = default);
}
