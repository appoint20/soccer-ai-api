namespace SoccerAi.Application.Entities;

/// <summary>
/// A ticket exactly as it was published, and how it finished.
///
/// This table is the product's evidence. Without it there is no way to tell a
/// customer what the strategy actually returned — only what a backtest says it
/// would have returned, which is not the same claim.
///
/// The prices and probabilities here are frozen at publication. Re-reading them
/// at settlement would silently measure the closing line instead of the price
/// the customer was shown, which flatters the record.
/// </summary>
public class PublishedTicket
{
    public int Id { get; set; }

    /// <summary>Midnight UTC of the board this ticket belonged to.</summary>
    public DateTimeOffset BoardDateUtc { get; set; }

    /// <summary>single | same_match_pair | combo.</summary>
    public string Kind { get; set; } = "";

    /// <summary>
    /// Stable identity of the ticket's shape (date, kind, legs). Unique, so
    /// republishing a board updates nothing and duplicates nothing. Deliberately
    /// excludes odds: a price move does not make it a different ticket, and the
    /// first published price is the one that must be kept.
    /// </summary>
    public string Fingerprint { get; set; } = "";

    public double TotalOdds { get; set; }
    public double CombinedProbability { get; set; }
    public double Ev { get; set; }

    /// <summary>Quarter-Kelly share of bankroll, as published.</summary>
    public double KellyStake { get; set; }

    /// <summary>pending | won | lost | void.</summary>
    public string Status { get; set; } = TicketStatus.Pending;

    public DateTimeOffset PublishedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SettledAtUtc { get; set; }

    public List<PublishedTicketLeg> Legs { get; set; } = [];
}

/// <summary>One selection inside a published ticket, frozen at publication.</summary>
public class PublishedTicketLeg
{
    public int Id { get; set; }
    public int PublishedTicketId { get; set; }

    public int FixtureId { get; set; }
    public string League { get; set; } = "";
    public string Market { get; set; } = "";
    public string Selection { get; set; } = "";

    public double Probability { get; set; }
    public double Odds { get; set; }
    public double Ev { get; set; }

    /// <summary>pending | won | lost | void.</summary>
    public string Status { get; set; } = TicketStatus.Pending;
}

public static class TicketStatus
{
    public const string Pending = "pending";
    public const string Won = "won";
    public const string Lost = "lost";
    public const string Void = "void";

    /// <summary>Statuses that represent a finished, countable result.</summary>
    public static bool IsSettled(string status) => status is Won or Lost;
}
