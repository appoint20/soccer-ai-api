using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Application.Services.Decisions;

/// <summary>
/// Records published tickets and settles them once the fixtures finish.
///
/// Two rules hold the integrity of the record:
///
/// 1. Prices are written once, at publication, and never refreshed. Re-reading
///    odds at settlement would measure the closing line rather than the price a
///    customer was shown — a change that always flatters the numbers.
/// 2. A settled ticket is never re-settled. Results are append-only history, not
///    a cache to be recomputed.
/// </summary>
public sealed class PickLedger(
    IApplicationDbContext dbContext,
    ILogger<PickLedger> logger) : IPickLedger
{
    /// <summary>Flat staking: one unit per ticket, so ROI is comparable across tickets.</summary>
    private const double FlatStake = 1.0;

    public async Task<int> RecordAsync(DailyPickBoard board, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(board);

        var boardDateUtc = new DateTimeOffset(board.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var candidates = board.Tickets
            .Select(t => (Ticket: t, Fingerprint: TicketFingerprint.Compute(boardDateUtc, KindOf(t), t.Legs)))
            .ToList();

        if (candidates.Count == 0) return 0;

        var fingerprints = candidates.Select(c => c.Fingerprint).ToList();
        var alreadyRecorded = await dbContext.PublishedTickets
            .Where(t => fingerprints.Contains(t.Fingerprint))
            .Select(t => t.Fingerprint)
            .ToListAsync(ct);

        var recorded = alreadyRecorded.ToHashSet(StringComparer.Ordinal);
        var newTickets = candidates
            .Where(c => !recorded.Contains(c.Fingerprint))
            .Select(c => ToEntity(c.Ticket, c.Fingerprint, boardDateUtc))
            .ToList();

        if (newTickets.Count == 0)
        {
            logger.LogInformation("[Ledger] {Date}: board already recorded, nothing added", board.Date);
            return 0;
        }

        dbContext.PublishedTickets.AddRange(newTickets);
        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("[Ledger] {Date}: recorded {New} new tickets ({Existing} already present)",
            board.Date, newTickets.Count, candidates.Count - newTickets.Count);

        return newTickets.Count;
    }

    public async Task<int> SettleAsync(CancellationToken ct = default)
    {
        var pending = await dbContext.PublishedTickets
            .Include(t => t.Legs)
            .Where(t => t.Status == TicketStatus.Pending)
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        var fixtureIds = pending.SelectMany(t => t.Legs).Select(l => l.FixtureId).Distinct().ToList();
        var fixtures = await dbContext.Fixtures
            .AsNoTracking()
            .Where(f => fixtureIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);

        var settledCount = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var ticket in pending)
        {
            foreach (var leg in ticket.Legs)
            {
                leg.Status = SettleLeg(leg, fixtures.GetValueOrDefault(leg.FixtureId));
            }

            var outcome = MarketOutcome.Combine(ticket.Legs.Select(ToOutcome));
            if (outcome == SelectionOutcome.Pending) continue;

            ticket.Status = ToStatus(outcome);
            ticket.SettledAtUtc = now;
            settledCount++;
        }

        if (settledCount > 0)
        {
            await dbContext.SaveChangesAsync(ct);
            logger.LogInformation("[Ledger] Settled {Count} tickets ({StillPending} still pending)",
                settledCount, pending.Count - settledCount);
        }

        return settledCount;
    }

    public async Task<PickPerformance> GetPerformanceAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toUtc = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(1);

        var tickets = await dbContext.PublishedTickets
            .AsNoTracking()
            .Include(t => t.Legs)
            .Where(t => t.BoardDateUtc >= fromUtc && t.BoardDateUtc < toUtc)
            .ToListAsync(ct);

        return new PickPerformance(
            from,
            to,
            Summarize("overall", tickets),
            [.. tickets.GroupBy(t => t.Kind).Select(g => Summarize(g.Key, g)).OrderBy(s => s.Key)],
            [.. MarketSlices(tickets)],
            [.. WeeklySlices(tickets)]);
    }

    /// <summary>
    /// Realized profit per ISO week, oldest first.
    /// </summary>
    /// <remarks>
    /// Profit is expressed in stakes rather than money: the ledger stakes one
    /// flat unit per ticket, so dividing by that unit gives a figure that means
    /// the same thing to a reader betting 1 as to one betting 100. These are
    /// live settled results, not a simulation — unlike the backtest report's
    /// weekly breakdown, which must never be shown beside them unlabelled.
    /// </remarks>
    private static IEnumerable<PickWeeklySlice> WeeklySlices(IReadOnlyCollection<PublishedTicket> tickets) =>
        tickets
            .GroupBy(t => ISOWeek.GetWeekOfYear(t.BoardDateUtc.UtcDateTime))
            .Select(g =>
            {
                var week = Summarize("week", g);
                return new
                {
                    Start = g.Min(t => t.BoardDateUtc),
                    Slice = new PickWeeklySlice(
                        $"W{g.Key:D2}",
                        week.Settled,
                        Math.Round((week.Returned - week.Staked) / FlatStake, 2)),
                };
            })
            .OrderBy(x => x.Start)
            .Select(x => x.Slice);

    // ── Summaries ────────────────────────────────────────────────────────────

    /// <summary>
    /// Per-market view. A combo touches several markets, so it is attributed to
    /// each of them; the per-market counts therefore sum to more than the ticket
    /// total. That is intended — the question being answered is "how do tickets
    /// containing BTTS do", not "how many tickets exist".
    /// </summary>
    private static IEnumerable<PickPerformanceSlice> MarketSlices(IReadOnlyCollection<PublishedTicket> tickets) =>
        tickets
            .SelectMany(t => t.Legs.Select(l => l.Market).Distinct().Select(market => (market, ticket: t)))
            .GroupBy(x => x.market)
            .Select(g => Summarize(g.Key, g.Select(x => x.ticket)))
            .OrderBy(s => s.Key);

    private static PickPerformanceSlice Summarize(string key, IEnumerable<PublishedTicket> source)
    {
        var tickets = source as IReadOnlyCollection<PublishedTicket> ?? source.ToList();

        var settled = tickets.Where(t => TicketStatus.IsSettled(t.Status)).ToList();
        var won = settled.Where(t => t.Status == TicketStatus.Won).ToList();

        return new PickPerformanceSlice(
            key,
            settled.Count,
            won.Count,
            tickets.Count(t => t.Status == TicketStatus.Pending),
            tickets.Count(t => t.Status == TicketStatus.Void),
            settled.Count * FlatStake,
            won.Sum(t => t.TotalOdds * FlatStake));
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    private static string KindOf(Ticket ticket) =>
        ticket.IsSameMatchPair ? "same_match_pair"
        : ticket.IsSingle ? "single"
        : "combo";

    private static PublishedTicket ToEntity(Ticket ticket, string fingerprint, DateTimeOffset boardDateUtc) =>
        new()
        {
            BoardDateUtc = boardDateUtc,
            Kind = KindOf(ticket),
            Fingerprint = fingerprint,
            TotalOdds = ticket.TotalOdds,
            CombinedProbability = ticket.CombinedProbability,
            Ev = ticket.Ev,
            KellyStake = ticket.KellyStake,
            Status = TicketStatus.Pending,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            Legs = [.. ticket.Legs.Select(l => new PublishedTicketLeg
            {
                FixtureId = l.FixtureId,
                League = l.League,
                Market = l.Market,
                Selection = l.Selection,
                Probability = l.Probability,
                Odds = l.Odds,
                Ev = l.Ev,
                Status = TicketStatus.Pending
            })]
        };

    /// <summary>
    /// A leg whose fixture we no longer hold is void, never lost. Absent data is
    /// not evidence of a losing bet.
    /// </summary>
    private static string SettleLeg(PublishedTicketLeg leg, Fixture? fixture) =>
        fixture is null
            ? TicketStatus.Void
            : ToStatus(MarketOutcome.Settle(
                leg.Market, leg.Selection, fixture.Status, fixture.HomeGoal, fixture.AwayGoal));

    private static string ToStatus(SelectionOutcome outcome) => outcome switch
    {
        SelectionOutcome.Won => TicketStatus.Won,
        SelectionOutcome.Lost => TicketStatus.Lost,
        SelectionOutcome.Void => TicketStatus.Void,
        _ => TicketStatus.Pending
    };

    private static SelectionOutcome ToOutcome(PublishedTicketLeg leg) => leg.Status switch
    {
        TicketStatus.Won => SelectionOutcome.Won,
        TicketStatus.Lost => SelectionOutcome.Lost,
        TicketStatus.Void => SelectionOutcome.Void,
        _ => SelectionOutcome.Pending
    };
}
