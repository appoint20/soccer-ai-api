using Mediator.Net.Context;
using Mediator.Net.Contracts;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Application.Features.Picks;

/// <summary>
/// Reports what published tickets actually returned.
///
/// This is the only endpoint that can answer "does it work" with evidence
/// rather than simulation, so it reports honestly: void tickets are excluded
/// from ROI instead of being counted as losses, and every slice states whether
/// its sample is large enough to mean anything.
/// </summary>
public sealed class GetPickPerformanceHandler(IPickLedger ledger)
    : IRequestHandler<GetPickPerformanceQuery, GetPickPerformanceResponse>
{
    /// <summary>
    /// Below this many settled tickets, ROI is dominated by variance. Thirty is
    /// the conventional floor for treating a mean as informative, and betting
    /// returns are more volatile than most — so it is a floor, not a target.
    /// </summary>
    private const int MinimumInformativeSample = 30;

    /// <summary>Default window: the last 90 days of published boards.</summary>
    private const int DefaultWindowDays = 90;

    public async Task<GetPickPerformanceResponse> Handle(
        IReceiveContext<GetPickPerformanceQuery> context,
        CancellationToken cancellationToken)
    {
        var query = context.Message;
        var to = query.To ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var from = query.From ?? to.AddDays(-DefaultWindowDays);

        var performance = await ledger.GetPerformanceAsync(from, to, cancellationToken);

        return new GetPickPerformanceResponse
        {
            From = performance.From,
            To = performance.To,
            Overall = ToDto(performance.Overall),
            ByKind = [.. performance.ByKind.Select(ToDto)],
            ByMarket = [.. performance.ByMarket.Select(ToDto)],
            ByWeek = [.. performance.ByWeek.Select(ToWeeklyDto)]
        };
    }

    private static WeeklyPerformanceDto ToWeeklyDto(PickWeeklySlice week) => new()
    {
        Label = week.Label,
        Settled = week.Settled,
        ProfitUnits = week.ProfitUnits
    };

    private static PerformanceSliceDto ToDto(PickPerformanceSlice slice) => new()
    {
        Key = slice.Key,
        Settled = slice.Settled,
        Won = slice.Won,
        Pending = slice.Pending,
        Voided = slice.Voided,
        HitRatePct = Math.Round(slice.HitRate * 100, 1),
        Staked = Math.Round(slice.Staked, 2),
        Returned = Math.Round(slice.Returned, 2),
        RoiPct = Math.Round(slice.Roi * 100, 1),
        SampleTooSmall = slice.Settled < MinimumInformativeSample
    };
}
