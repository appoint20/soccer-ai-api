namespace SoccerAi.Application.Services.Decisions;

/// <summary>How a selection finished.</summary>
public enum SelectionOutcome
{
    /// <summary>The fixture has not produced a settleable result yet.</summary>
    Pending,

    Won,
    Lost,

    /// <summary>
    /// No stake is at risk: the fixture was abandoned, or the result cannot be
    /// settled honestly (see <see cref="MarketOutcome"/> on extra time).
    /// </summary>
    Void
}

/// <summary>
/// The single definition of whether a selection won.
///
/// It lives here rather than inside the backtest because the backtest and the
/// live ledger must agree by construction. Two copies of this logic would drift,
/// and the first symptom would be a published ROI that nobody can reproduce.
///
/// Pure: score in, outcome out. No database, no clock.
/// </summary>
public static class MarketOutcome
{
    /// <summary>
    /// The backtest's reporting alias for Under 2.5, settled identically.
    /// </summary>
    public const string LowScoring = "low_scoring";

    /// <summary>Statuses whose 90-minute score is final and trustworthy.</summary>
    private static readonly string[] SettleableStatuses = ["FT"];

    /// <summary>
    /// Statuses where no stake is at risk.
    ///
    /// AET and PEN are void here on purpose. The stored goals include extra
    /// time, but bookmakers settle goals markets on 90 minutes, and the
    /// 90-minute score is not recoverable from what is persisted. Marking a
    /// 1-1 that finished 3-2 after extra time as an Over 2.5 win would inflate
    /// the record with results a customer never got paid on. Voiding is the
    /// honest option — these fixtures are excluded from ROI rather than guessed.
    /// </summary>
    private static readonly string[] VoidStatuses =
        ["AET", "PEN", "PST", "CANC", "ABD", "SUSP", "INT", "AWD", "WO", "TBD"];

    public static SelectionOutcome Settle(
        string market, string selection, string status, int homeGoals, int awayGoals)
    {
        if (VoidStatuses.Contains(status)) return SelectionOutcome.Void;
        if (!SettleableStatuses.Contains(status)) return SelectionOutcome.Pending;

        var won = Won(market, selection, homeGoals, awayGoals);
        return won is null
            ? SelectionOutcome.Void         // market we cannot settle — never a silent loss
            : won.Value ? SelectionOutcome.Won : SelectionOutcome.Lost;
    }

    /// <summary>
    /// Did the selection land, given a final 90-minute score? Null means the
    /// market is not settleable from a score alone.
    /// </summary>
    public static bool? Won(string market, string selection, int homeGoals, int awayGoals)
    {
        var totalGoals = homeGoals + awayGoals;

        return market switch
        {
            ConfluenceRuleEngine.Markets.Btts => homeGoals > 0 && awayGoals > 0,
            ConfluenceRuleEngine.Markets.Over25 => totalGoals > 2,
            ConfluenceRuleEngine.Markets.Under25 => totalGoals < 3,
            LowScoring => totalGoals < 3,
            ConfluenceRuleEngine.Markets.Goals23 => totalGoals is 2 or 3,
            ConfluenceRuleEngine.Markets.Draw => homeGoals == awayGoals,
            ConfluenceRuleEngine.Markets.MatchWinner => WinnerWon(selection, homeGoals, awayGoals),
            _ => null
        };
    }

    /// <summary>
    /// The 1X2 side is carried in the selection label, because the market key
    /// alone does not say which side the rule engine evaluated. An unrecognised
    /// label returns null rather than defaulting to a side — a coin flip here
    /// would corrupt the record.
    /// </summary>
    private static bool? WinnerWon(string selection, int homeGoals, int awayGoals) => selection switch
    {
        ConfluenceRuleEngine.Selections.MatchWinnerHome => homeGoals > awayGoals,
        ConfluenceRuleEngine.Selections.MatchWinnerAway => awayGoals > homeGoals,
        _ => null
    };

    /// <summary>
    /// A ticket's outcome from its legs.
    ///
    /// Every leg must win. A void leg voids the whole ticket rather than
    /// re-pricing the remainder: the reduced-odds ticket is not the one that was
    /// published, and recording a result for a price never offered would be
    /// inventing history. Voided tickets are excluded from ROI.
    /// </summary>
    public static SelectionOutcome Combine(IEnumerable<SelectionOutcome> legs)
    {
        ArgumentNullException.ThrowIfNull(legs);

        var outcomes = legs as IReadOnlyCollection<SelectionOutcome> ?? legs.ToList();
        if (outcomes.Count == 0) return SelectionOutcome.Void;

        if (outcomes.Contains(SelectionOutcome.Pending)) return SelectionOutcome.Pending;
        if (outcomes.Contains(SelectionOutcome.Void)) return SelectionOutcome.Void;

        return outcomes.All(o => o == SelectionOutcome.Won)
            ? SelectionOutcome.Won
            : SelectionOutcome.Lost;
    }
}
