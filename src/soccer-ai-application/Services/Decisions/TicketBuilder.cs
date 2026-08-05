using SoccerAi.Application.Options;

namespace SoccerAi.Application.Services.Decisions;

/// <summary>One leg of a ticket (single, same-match pair, or accumulator).</summary>
public sealed record TicketLeg(
    int FixtureId,
    string League,
    string Market,
    string Selection,
    double Probability,
    double Odds,
    double Ev)
{
    public bool IsGoalsMarket => Market is "btts" or "over25";
}

/// <summary>
/// A same-match BTTS + Over 2.5 pair. Its probability is the TRUE joint from
/// the Dixon-Coles score matrix (the two markets are strongly correlated —
/// multiplying them would badly understate the chance), while the price is the
/// product of the two leg odds. Bookmakers price same-game doubles BELOW that
/// product, so <see cref="RequiredOdds"/> states the minimum price worth taking.
/// </summary>
public sealed record SameMatchPair(
    int FixtureId,
    string League,
    double JointProbability,
    double BttsOdds,
    double Over25Odds);

/// <summary>A sellable ticket: 1 leg (single), a same-match pair, or 2-3 legs.</summary>
public sealed record Ticket(
    IReadOnlyList<TicketLeg> Legs,
    double TotalOdds,
    double CombinedProbability,
    double Ev,
    double KellyStake,
    bool IsSameMatchPair = false)
{
    public bool IsSingle => Legs.Count == 1;
    public bool ContainsGoalsMarket => Legs.Any(l => l.IsGoalsMarket);

    /// <summary>Fair price for this ticket's probability — never accept less.</summary>
    public double FairOdds => CombinedProbability > 0 ? Math.Round(1 / CombinedProbability, 2) : 0;
}

/// <summary>
/// Ticket economics with goals-market priority.
///
/// Rules (product):
/// - Every ticket must reach the minimum price: 1.70 normally, 1.85 for
///   same-match BTTS+Over2.5 pairs, 2.10 when a 1X2 leg is involved.
/// - A "sure" match priced below 1.70 is not discarded: its BTTS and Over 2.5
///   are paired into a same-match ticket to lift the price, and that pair can
///   then be combined with another match.
/// - BTTS / Over 2.5 tickets are built first and guaranteed up to
///   <see cref="ConfluenceOptions.MinGoalsMarketTickets"/> slots per day.
/// - Legs need EV &gt; 0 + full confluence; never two markets from different
///   fixtures in the same league; one fixture contributes one leg (except the
///   same-match pair, which is itself one leg group).
/// </summary>
public static class TicketBuilder
{
    public const int MinComboLegs = 2;
    public const int MaxSupportedComboLegs = 3;
    public const int MaxComboTicketsPerDay = 5;
    public const double PreferredFavoriteProbability = 0.65;

    public static double MarketFloor(string market, StrategyOptions strat) => market switch
    {
        "match_winner" or "draw" => strat.MinOdds1X2,
        "btts" => strat.MinOddsBtts,
        "over25" => strat.MinOddsOver25,
        "under25" => strat.MinOddsUnder25,
        "goals_2_3" => strat.MinOddsGoals23,
        _ => strat.MinOddsBtts
    };

    /// <summary>Ticket floor = the strictest of its legs' market-group floors.</summary>
    public static double TicketFloor(IReadOnlyCollection<TicketLeg> legs, StrategyOptions strat) =>
        legs.Max(l => MarketFloor(l.Market, strat));

    public static List<Ticket> Build(
        IReadOnlyList<TicketLeg> qualifiedSingles,
        IReadOnlyList<TicketLeg> comboEligibleLegs,
        StrategyOptions strat,
        ConfluenceOptions opt,
        IReadOnlyList<SameMatchPair>? sameMatchPairs = null)
    {
        var tickets = new List<Ticket>();

        // ── 1. Singles that already clear their own market floor ──
        foreach (var leg in qualifiedSingles.Where(l => l.Odds >= MarketFloor(l.Market, strat)))
            tickets.Add(MakeTicket([leg], opt));

        // ── 2. Same-match BTTS+Over2.5 pairs (rescues sub-floor "sure" matches) ──
        foreach (var pair in sameMatchPairs ?? [])
        {
            var totalOdds = pair.BttsOdds * pair.Over25Odds;
            if (totalOdds < strat.MinOddsSameMatchPair) continue;

            var ev = pair.JointProbability * totalOdds - 1;
            if (ev <= 0) continue;

            var legs = new List<TicketLeg>
            {
                new(pair.FixtureId, pair.League, "btts", "BTTS",
                    pair.JointProbability, pair.BttsOdds, ev),
                new(pair.FixtureId, pair.League, "over25", "Over 2.5 Goals",
                    pair.JointProbability, pair.Over25Odds, ev)
            };

            tickets.Add(new Ticket(
                legs,
                Math.Round(totalOdds, 2),
                Math.Round(pair.JointProbability, 4),
                Math.Round(ev, 4),
                ValueMath.FractionalKelly(pair.JointProbability, totalOdds, opt.KellyFraction),
                IsSameMatchPair: true));
        }

        // ── 3. Multi-match combos ──
        var pool = comboEligibleLegs
            .GroupBy(l => l.FixtureId)
            .Select(g => g.OrderByDescending(l => l.IsGoalsMarket).ThenByDescending(l => l.Ev).First())
            .OrderByDescending(l => l.IsGoalsMarket)                       // goals markets first
            .ThenByDescending(l => l.Probability >= PreferredFavoriteProbability)
            .ThenByDescending(l => l.Ev)
            .Take(12)
            .ToList();

        var combos = new List<Ticket>();
        ComposeCombos(pool, [], 0, strat, opt, combos);

        // Goals-market tickets get guaranteed slots, then the rest by EV.
        var goalsCombos = combos.Where(t => t.ContainsGoalsMarket)
            .OrderByDescending(t => t.Legs.Any(l => l.Probability >= PreferredFavoriteProbability))
            .ThenByDescending(t => t.Ev)
            .Take(opt.MinGoalsMarketTickets)
            .ToList();

        var otherCombos = combos
            .Except(goalsCombos)
            .OrderByDescending(t => t.Legs.Any(l => l.Probability >= PreferredFavoriteProbability))
            .ThenByDescending(t => t.Ev)
            .Take(Math.Max(0, MaxComboTicketsPerDay - goalsCombos.Count))
            .ToList();

        tickets.AddRange(goalsCombos);
        tickets.AddRange(otherCombos);

        return tickets;
    }

    private static void ComposeCombos(
        List<TicketLeg> pool, List<TicketLeg> current, int start,
        StrategyOptions strat, ConfluenceOptions opt, List<Ticket> results)
    {
        if (current.Count >= MinComboLegs)
        {
            var totalOdds = current.Aggregate(1.0, (acc, l) => acc * l.Odds);
            if (totalOdds >= TicketFloor(current, strat))
            {
                var ticket = MakeTicket([.. current], opt);
                if (ticket.Ev > 0)
                    results.Add(ticket);
            }
        }

        var maxLegs = Math.Clamp(opt.MaxComboLegs, MinComboLegs, MaxSupportedComboLegs);
        if (current.Count == maxLegs || results.Count >= 200) return;

        for (var i = start; i < pool.Count; i++)
        {
            var leg = pool[i];
            if (current.Any(l => l.FixtureId == leg.FixtureId)) continue; // one leg per fixture
            if (current.Any(l => l.League == leg.League)) continue;       // one leg per league

            current.Add(leg);
            ComposeCombos(pool, current, i + 1, strat, opt, results);
            current.RemoveAt(current.Count - 1);
        }
    }

    private static Ticket MakeTicket(IReadOnlyList<TicketLeg> legs, ConfluenceOptions opt)
    {
        var totalOdds = legs.Aggregate(1.0, (acc, l) => acc * l.Odds);
        // Legs come from distinct fixtures → independence is a fair approximation.
        var combinedP = legs.Aggregate(1.0, (acc, l) => acc * l.Probability);
        var ev = combinedP * totalOdds - 1;

        return new Ticket(
            legs,
            Math.Round(totalOdds, 2),
            Math.Round(combinedP, 4),
            Math.Round(ev, 4),
            ValueMath.FractionalKelly(combinedP, totalOdds, opt.KellyFraction));
    }
}
