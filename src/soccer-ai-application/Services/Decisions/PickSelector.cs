using SoccerAi.Application.Models;
using SoccerAi.Application.Options;

namespace SoccerAi.Application.Services.Decisions;

/// <summary>Identity of the fixture a selection belongs to.</summary>
public sealed record FixtureRef(
    int FixtureId,
    string League,
    string HomeTeam,
    string AwayTeam,
    DateTimeOffset KickoffUtc)
{
    public string Match => $"{HomeTeam} vs {AwayTeam}";
}

/// <summary>
/// Product 2: the single most likely market on a fixture, published without
/// any odds requirement.
///
/// Beware when displaying <see cref="Probability"/>: taking the maximum across
/// several markets is upward biased, because the winner of a comparison sits
/// above the average of the estimates compared. The honest number to publish
/// is the measured hit rate for the probability bucket, which the backtest
/// report computes.
/// </summary>
public sealed record ConfidencePick(
    FixtureRef Fixture,
    string Market,
    string Selection,
    double Probability);

/// <summary>
/// Everything one fixture offers a ticket builder, with no reference to the
/// outcome. Keeping outcomes out is what lets the backtest and the live
/// endpoint run the exact same selection code: the backtest joins results back
/// on (FixtureId, Market) afterwards.
/// </summary>
/// <param name="UnpricedComboLegs">
/// Legs that passed every check except having a price. Kept apart from the
/// priced lists so nothing can accidentally stake one.
/// </param>
public sealed record FixtureSelection(
    FixtureRef Fixture,
    IReadOnlyList<TicketLeg> QualifiedLegs,
    IReadOnlyList<TicketLeg> ComboEligibleLegs,
    SameMatchPair? SameMatchPair,
    ConfidencePick? ConfidencePick,
    IReadOnlyList<TicketLeg>? UnpricedComboLegs = null)
{
    public static FixtureSelection Empty(FixtureRef fixture) => new(fixture, [], [], null, null);
}

/// <summary>
/// Turns an audited decision into sellable selections. Pure and stateless: the
/// <see cref="DecisionAudit"/> already carries probability, price, EV, Kelly
/// stake and gate outcome per market, so nothing here re-derives probabilities
/// — it only chooses what to offer.
///
/// This is the single selection algorithm in the system. The backtest and the
/// live picks endpoint both call it, which is the only way the published picks
/// can be trusted to match what was measured.
/// </summary>
public static class PickSelector
{
    /// <summary>Markets eligible for a Product 2 confidence pick, in no order.</summary>
    private static readonly string[] ConfidenceMarkets =
    [
        ConfluenceRuleEngine.Markets.Btts,
        ConfluenceRuleEngine.Markets.Over25,
        ConfluenceRuleEngine.Markets.Under25,
        ConfluenceRuleEngine.Markets.MatchWinner
    ];

    /// <summary>
    /// Label to use when an audit predates the <see cref="MarketRuleAudit.Selection"/>
    /// field. The 1X2 side is unknowable from an old snapshot, so it degrades to
    /// the neutral market name rather than guessing a side.
    /// </summary>
    public static string DefaultSelectionFor(string market) => market switch
    {
        ConfluenceRuleEngine.Markets.Btts => ConfluenceRuleEngine.Selections.Btts,
        ConfluenceRuleEngine.Markets.Over25 => ConfluenceRuleEngine.Selections.Over25,
        ConfluenceRuleEngine.Markets.Under25 => ConfluenceRuleEngine.Selections.Under25,
        ConfluenceRuleEngine.Markets.Goals23 => ConfluenceRuleEngine.Selections.Goals23,
        ConfluenceRuleEngine.Markets.Draw => ConfluenceRuleEngine.Selections.Draw,
        ConfluenceRuleEngine.Markets.MatchWinner => "Match Winner",
        _ => market
    };

    public static string SelectionOf(MarketRuleAudit audit) =>
        string.IsNullOrWhiteSpace(audit.Selection)
            ? DefaultSelectionFor(audit.Market)
            : audit.Selection;

    /// <param name="fixture">The fixture the audit belongs to.</param>
    /// <param name="audit">Rule-engine output per market; null selects nothing.</param>
    /// <param name="bttsAndOver25JointProbability">
    /// The true joint P(BTTS ∧ Over 2.5) read off the Dixon-Coles score matrix.
    /// Pass null when unavailable; never pass p_btts × p_over25, which badly
    /// understates it because the two markets are positively correlated.
    /// </param>
    /// <param name="opt">Confluence thresholds governing qualification.</param>
    public static FixtureSelection Select(
        FixtureRef fixture,
        DecisionAudit? audit,
        double? bttsAndOver25JointProbability,
        ConfluenceOptions opt)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(opt);

        if (audit is null || audit.Markets.Count == 0)
            return FixtureSelection.Empty(fixture);

        var qualified = new List<TicketLeg>();
        var comboEligible = new List<TicketLeg>();
        var unpriced = new List<TicketLeg>();

        foreach (var market in audit.Markets)
        {
            var leg = ToLeg(fixture, market);
            if (leg is null)
            {
                if (opt.AllowUnpricedCombos &&
                    ToUnpricedLeg(fixture, market, audit.MinConfirmationsRequired, opt) is { } unpricedLeg)
                {
                    unpriced.Add(unpricedLeg);
                }

                continue;
            }

            if (market.Qualified) qualified.Add(leg);
            if (market.ComboEligible) comboEligible.Add(leg);
        }

        return new FixtureSelection(
            fixture,
            qualified,
            comboEligible,
            BuildSameMatchPair(fixture, audit, bttsAndOver25JointProbability),
            BuildConfidencePick(fixture, audit, opt),
            unpriced);
    }

    /// <summary>
    /// Assembles the day's board from per-fixture selections. Delegates every
    /// pricing and shape rule to <see cref="TicketBuilder"/> so the floors, the
    /// goals-market priority and the leg limits live in exactly one place.
    /// </summary>
    public static List<Ticket> BuildTickets(
        IEnumerable<FixtureSelection> selections,
        StrategyOptions strat,
        ConfluenceOptions opt)
    {
        ArgumentNullException.ThrowIfNull(selections);

        var all = selections as IReadOnlyCollection<FixtureSelection> ?? selections.ToList();

        return TicketBuilder.Build(
            all.SelectMany(s => s.QualifiedLegs).ToList(),
            all.SelectMany(s => s.ComboEligibleLegs).ToList(),
            strat,
            opt,
            all.Select(s => s.SameMatchPair).OfType<SameMatchPair>().ToList(),
            all.SelectMany(s => s.UnpricedComboLegs ?? []).ToList());
    }

    // ── Internals ────────────────────────────────────────────────────────────

    /// <summary>
    /// A leg needs a real price. Markets without valid odds are analysis-only
    /// by design — they are never priced, so they can never be staked.
    /// </summary>
    private static TicketLeg? ToLeg(FixtureRef fixture, MarketRuleAudit market)
    {
        var odds = OddsGuard.Sanitize(market.Odds);
        if (odds is null) return null;

        return new TicketLeg(
            fixture.FixtureId,
            fixture.League,
            market.Market,
            SelectionOf(market),
            market.Probability,
            odds.Value,
            market.Ev ?? ValueMath.Ev(market.Probability, odds.Value));
    }

    /// <summary>
    /// A leg the model would have taken had anyone quoted a price.
    /// </summary>
    /// <remarks>
    /// The value gate reports <c>analysis_only_no_odds</c> before it looks at
    /// probability, vetoes or confirmations, so a missing quote hides whether
    /// the rest of the confluence passed. This re-applies exactly those
    /// non-price checks and nothing else: the bar is not lowered, the price
    /// requirement is simply not applied.
    ///
    /// Informational markets stay excluded — 2-3 goals can never become a bet,
    /// and a missing price is not a reason to promote one.
    /// </remarks>
    private static TicketLeg? ToUnpricedLeg(
        FixtureRef fixture, MarketRuleAudit market, int minConfirmations, ConfluenceOptions opt)
    {
        if (OddsGuard.Sanitize(market.Odds) is not null) return null;
        if (opt.InformationalOnlyMarkets.Contains(market.Market)) return null;

        if (!market.ProbabilityPassed) return null;
        if (market.VetoesFired > 0) return null;
        if (market.ConfirmationsFired < minConfirmations) return null;

        return new TicketLeg(
            fixture.FixtureId,
            fixture.League,
            market.Market,
            SelectionOf(market),
            market.Probability,
            Odds: null,
            Ev: null);
    }

    /// <summary>
    /// A same-match BTTS + Over 2.5 double. This exists to rescue a fixture the
    /// model likes but the bookmaker prices below the single-bet floor: pairing
    /// the two correlated goals markets lifts the price without reaching for a
    /// selection the model does not believe in.
    ///
    /// Both legs must be combo-eligible on their own — pairing two bets we would
    /// not otherwise take would multiply their errors, not cancel them.
    /// </summary>
    private static SameMatchPair? BuildSameMatchPair(
        FixtureRef fixture, DecisionAudit audit, double? jointProbability)
    {
        if (jointProbability is not > 0) return null;

        var btts = audit.Markets.FirstOrDefault(m => m.Market == ConfluenceRuleEngine.Markets.Btts);
        var over25 = audit.Markets.FirstOrDefault(m => m.Market == ConfluenceRuleEngine.Markets.Over25);

        if (btts is not { ComboEligible: true } || over25 is not { ComboEligible: true })
            return null;

        var bttsOdds = OddsGuard.Sanitize(btts.Odds);
        var over25Odds = OddsGuard.Sanitize(over25.Odds);
        if (bttsOdds is null || over25Odds is null) return null;

        return new SameMatchPair(
            fixture.FixtureId, fixture.League,
            jointProbability.Value, bttsOdds.Value, over25Odds.Value);
    }

    /// <summary>
    /// Minimum probability this market must reach to be publishable, falling
    /// back to the global floor.
    /// </summary>
    public static double ConfidenceFloorFor(string market, ConfluenceOptions opt) =>
        opt.ConfidencePickMinProbabilityByMarket.TryGetValue(market, out var floor)
            ? floor
            : opt.ConfidencePickMinProbability;

    /// <summary>
    /// Each market is tested against its own floor <em>before</em> the best is
    /// chosen, not after.
    ///
    /// The order matters. Selecting first and filtering second would let a
    /// suppressed market win the comparison and then be dropped, silently
    /// costing the fixture a perfectly publishable pick from another market.
    /// </summary>
    private static ConfidencePick? BuildConfidencePick(
        FixtureRef fixture, DecisionAudit audit, ConfluenceOptions opt)
    {
        var best = audit.Markets
            .Where(m => ConfidenceMarkets.Contains(m.Market))
            .Where(m => m.Probability >= ConfidenceFloorFor(m.Market, opt))
            .OrderByDescending(m => m.Probability)
            .FirstOrDefault();

        return best is null
            ? null
            : new ConfidencePick(fixture, best.Market, SelectionOf(best), best.Probability);
    }
}
