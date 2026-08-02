using SoccerAi.Application.Options;
using SoccerAi.Application.Services;

namespace SoccerAi.Application.Services.Decisions;

/// <summary>One leg of a ticket (single or accumulator).</summary>
public sealed record TicketLeg(
    int FixtureId,
    string League,
    string Market,
    string Selection,
    double Probability,
    double Odds,
    double Ev);

/// <summary>A sellable ticket: 1 leg (single) or 2-3 legs (combo).</summary>
public sealed record Ticket(
    IReadOnlyList<TicketLeg> Legs,
    double TotalOdds,
    double CombinedProbability,
    double Ev,
    double KellyStake)
{
    public bool IsSingle => Legs.Count == 1;
}

/// <summary>
/// Ticket economics (v5): MinOdds floors apply to the TICKET, not the leg.
/// - Single = one QUALIFIED pick whose odds meet its market-group floor.
/// - Combo = 2-3 combo-eligible legs (EV &gt; 0 + confluence): never two
///   markets from the same fixture, max 1 leg per league per ticket, product
///   of odds ≥ floor (2.10 when any 1X2 leg is involved, else 1.70).
/// - Preference: calibrated-p ≥ 0.65 favorites first, then EV.
/// - Kelly is computed at TICKET level from combined probability × total odds.
/// </summary>
public static class TicketBuilder
{
    public const int MinComboLegs = 2;

    /// <summary>Hard ceiling; the effective limit is ConfluenceOptions.MaxComboLegs.</summary>
    public const int MaxSupportedComboLegs = 3;

    public const int MaxComboTicketsPerDay = 5;
    public const double PreferredFavoriteProbability = 0.65;

    private static readonly string[] OneXTwoMarkets = ["match_winner", "draw"];

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
        ConfluenceOptions opt)
    {
        var tickets = new List<Ticket>();

        // ── Singles: qualified picks meeting their own market floor ──
        foreach (var leg in qualifiedSingles.Where(l => l.Odds >= MarketFloor(l.Market, strat)))
            tickets.Add(MakeTicket([leg], opt));

        // ── Combos: preference-ordered pool, one leg per fixture AND league ──
        var pool = comboEligibleLegs
            .GroupBy(l => l.FixtureId)
            .Select(g => g.OrderByDescending(l => l.Ev).First()) // best market per fixture
            .OrderByDescending(l => l.Probability >= PreferredFavoriteProbability) // favorites first
            .ThenByDescending(l => l.Ev)
            .Take(12)
            .ToList();

        var combos = new List<Ticket>();
        ComposeCombos(pool, [], 0, strat, opt, combos);

        // Preference applies to the FINAL ranking, not just the pool: pure-EV
        // ordering would always favor 3-leg longshot products over favorite
        // combos (EV multiplies), silently deleting the ≥65% preference.
        tickets.AddRange(combos
            .OrderByDescending(t => t.Legs.Any(l => l.Probability >= PreferredFavoriteProbability))
            .ThenByDescending(t => t.Ev)
            .Take(MaxComboTicketsPerDay));

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
            if (current.Any(l => l.FixtureId == leg.FixtureId)) continue; // one market per fixture
            if (current.Any(l => l.League == leg.League)) continue;       // max 1 leg per league

            current.Add(leg);
            ComposeCombos(pool, current, i + 1, strat, opt, results);
            current.RemoveAt(current.Count - 1);
        }
    }

    private static Ticket MakeTicket(IReadOnlyList<TicketLeg> legs, ConfluenceOptions opt)
    {
        var totalOdds = legs.Aggregate(1.0, (acc, l) => acc * l.Odds);
        // Legs are from distinct fixtures → independence is a fair approximation.
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
