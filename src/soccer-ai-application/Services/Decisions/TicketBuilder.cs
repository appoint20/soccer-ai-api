using SoccerAi.Application.Options;

namespace SoccerAi.Application.Services.Decisions;

/// <summary>One leg of a ticket (single, same-match pair, or accumulator).</summary>
/// <param name="Odds">
/// The bookmaker's price, or null when none was published. Null is deliberate:
/// a synthesised price would make an unbettable leg look stakeable.
/// </param>
/// <param name="Ev">Expected value, null whenever <paramref name="Odds"/> is.</param>
public sealed record TicketLeg(
    int FixtureId,
    string League,
    string Market,
    string Selection,
    double Probability,
    double? Odds,
    double? Ev)
{
    /// <summary>Whether a real quoted price backs this leg.</summary>
    public bool IsPriced => Odds.HasValue;
}

/// <summary>
/// A same-match BTTS + Over 2.5 pair. Its probability is the TRUE joint from
/// the Dixon-Coles score matrix (the two markets are strongly correlated —
/// multiplying them would badly understate the chance), while the price is the
/// product of the two leg odds. Bookmakers price same-game doubles BELOW that
/// product, so the pair's required odds state the minimum price worth taking.
/// </summary>
public sealed record SameMatchPair(
    int FixtureId,
    string League,
    double JointProbability,
    double BttsOdds,
    double Over25Odds);

/// <summary>A ticket: 1 leg (single), a same-match pair, or 2-3 legs.</summary>
/// <param name="TotalOdds">
/// Product of the leg prices, or null when any leg is unpriced. An unpriced
/// ticket is an analysis-only suggestion, not something that can be staked.
/// </param>
/// <param name="Ev">Expected value; null without a total price to compare against.</param>
/// <param name="KellyStake">Bankroll share; null without a price, since Kelly needs one.</param>
public sealed record Ticket(
    IReadOnlyList<TicketLeg> Legs,
    double? TotalOdds,
    double CombinedProbability,
    double? Ev,
    double? KellyStake,
    bool IsSameMatchPair = false)
{
    public bool IsSingle => Legs.Count == 1;

    /// <summary>
    /// Whether every leg carries a real price. Only a priced ticket may be
    /// staked, recorded in the ledger, or counted towards performance.
    /// </summary>
    public bool IsPriced => TotalOdds.HasValue;

    /// <summary>
    /// Whether this ticket touches a focus (goals) market. Set at construction
    /// because which markets count is configuration, not a fact about a leg.
    /// </summary>
    public bool ContainsGoalsMarket { get; init; }

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

    /// <summary>
    /// Legs considered when composing combos. Combinations grow factorially, so
    /// this caps the search rather than the product: twelve legs is already
    /// 220 three-leg candidates.
    /// </summary>
    public const int PoolSize = 12;

    /// <summary>
    /// Focus markets, which get guaranteed daily slots. Configuration rather
    /// than a constant: which markets deserve priority is a product decision
    /// that the measured results should be allowed to change.
    /// </summary>
    public static bool IsGoalsMarket(string market, ConfluenceOptions opt) =>
        opt.GoalsMarkets.Contains(market);

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

    /// <param name="unpricedComboLegs">
    /// Legs the model would have taken but for a missing quote. Used only to
    /// fill combo slots that priced tickets left empty — see
    /// <see cref="ConfluenceOptions.AllowUnpricedCombos"/>.
    /// </param>
    public static List<Ticket> Build(
        IReadOnlyList<TicketLeg> qualifiedSingles,
        IReadOnlyList<TicketLeg> comboEligibleLegs,
        StrategyOptions strat,
        ConfluenceOptions opt,
        IReadOnlyList<SameMatchPair>? sameMatchPairs = null,
        IReadOnlyList<TicketLeg>? unpricedComboLegs = null)
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
                IsSameMatchPair: true)
            {
                ContainsGoalsMarket = true
            });
        }

        // ── 3. Multi-match combos ──
        // Legs must clear the same bar as singles (see ComboLegsRequireQualified):
        // errors multiply in a parlay, so a weaker per-leg filter is backwards.
        var legSource = opt.ComboLegsRequireQualified ? qualifiedSingles : comboEligibleLegs;
        var pool = legSource
            .GroupBy(l => l.FixtureId)
            .Select(g => g.OrderByDescending(l => IsGoalsMarket(l.Market, opt))
                          .ThenByDescending(l => l.Ev).First())
            .OrderByDescending(l => IsGoalsMarket(l.Market, opt))           // focus markets first
            .ThenByDescending(l => l.Probability >= PreferredFavoriteProbability)
            .ThenByDescending(l => l.Ev)
            .Take(PoolSize)
            .ToList();

        var combos = new List<Ticket>();
        ComposeCombos(pool, [], 0, opt, combos,
            t => t.TotalOdds >= TicketFloor(t.Legs, strat) && t.Ev > 0);

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

        // ── 4. Unpriced combos ──
        // Only for slots priced tickets did not fill: a real price always wins.
        // These exist so the board is not empty on a day the odds feed has not
        // landed, and they are published as analysis, not as bets — no EV, no
        // Kelly, no total price, because none of those exist without a quote.
        var remainingSlots = MaxComboTicketsPerDay - goalsCombos.Count - otherCombos.Count;
        if (opt.AllowUnpricedCombos && remainingSlots > 0 && unpricedComboLegs is { Count: > 0 })
        {
            var unpricedPool = unpricedComboLegs
                .GroupBy(l => l.FixtureId)
                .Select(g => g.OrderByDescending(l => IsGoalsMarket(l.Market, opt))
                              .ThenByDescending(l => l.Probability).First())
                .OrderByDescending(l => IsGoalsMarket(l.Market, opt))
                .ThenByDescending(l => l.Probability)
                .Take(PoolSize)
                .ToList();

            var unpricedCombos = new List<Ticket>();
            ComposeCombos(unpricedPool, [], 0, opt, unpricedCombos,
                t => t.CombinedProbability >= opt.UnpricedComboMinProbability);

            tickets.AddRange(unpricedCombos
                .OrderByDescending(t => t.ContainsGoalsMarket)
                .ThenByDescending(t => t.CombinedProbability)
                .Take(remainingSlots));
        }

        return tickets;
    }

    /// <param name="accept">
    /// Whether a composed ticket is publishable. Passed in because the bar
    /// differs by ticket kind: a priced combo must clear its odds floor with
    /// positive EV, while an unpriced one has neither and is judged on
    /// probability alone.
    /// </param>
    private static void ComposeCombos(
        List<TicketLeg> pool, List<TicketLeg> current, int start,
        ConfluenceOptions opt, List<Ticket> results, Func<Ticket, bool> accept)
    {
        if (current.Count >= MinComboLegs)
        {
            var ticket = MakeTicket([.. current], opt);
            if (accept(ticket))
                results.Add(ticket);
        }

        var maxLegs = Math.Clamp(opt.MaxComboLegs, MinComboLegs, MaxSupportedComboLegs);
        if (current.Count == maxLegs || results.Count >= 200) return;

        for (var i = start; i < pool.Count; i++)
        {
            var leg = pool[i];

            // One leg per fixture. This is also what makes a self-cancelling
            // ticket impossible: Over 2.5 and Under 2.5 on the SAME match can
            // never both land, so such a combo is a guaranteed loss however
            // attractive its price looks. Across different matches they are
            // simply two independent bets, and that is allowed.
            //
            // Unlike the league limit below, this rule is arithmetic rather
            // than preference, so it is not configurable.
            if (current.Any(l => l.FixtureId == leg.FixtureId)) continue;

            if (opt.MaxLegsPerLeague > 0 &&
                current.Count(l => l.League == leg.League) >= opt.MaxLegsPerLeague) continue;

            current.Add(leg);
            ComposeCombos(pool, current, i + 1, opt, results, accept);
            current.RemoveAt(current.Count - 1);
        }
    }

    private static Ticket MakeTicket(IReadOnlyList<TicketLeg> legs, ConfluenceOptions opt)
    {
        // Legs come from distinct fixtures → independence is a fair approximation.
        var combinedP = legs.Aggregate(1.0, (acc, l) => acc * l.Probability);

        // One unpriced leg makes the total unknowable. Everything downstream of
        // a price — EV, Kelly — then stays null rather than being computed
        // against a number nobody quoted.
        double? totalOdds = legs.All(l => l.Odds.HasValue)
            ? legs.Aggregate(1.0, (acc, l) => acc * l.Odds!.Value)
            : null;

        return new Ticket(
            legs,
            totalOdds is null ? null : Math.Round(totalOdds.Value, 2),
            Math.Round(combinedP, 4),
            totalOdds is null ? null : Math.Round(combinedP * totalOdds.Value - 1, 4),
            totalOdds is null
                ? null
                : ValueMath.FractionalKelly(combinedP, totalOdds.Value, opt.KellyFraction))
        {
            ContainsGoalsMarket = legs.Any(l => IsGoalsMarket(l.Market, opt))
        };
    }
}
