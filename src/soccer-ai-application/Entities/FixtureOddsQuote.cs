namespace SoccerAi.Application.Entities;

/// <summary>
/// One bookmaker's price for one market outcome at capture time.
/// Insert-only: the first row per (fixture, bookmaker, market) is the opening
/// price, later rows are line movements — drift is derivable. The fixture's
/// odds columns always hold the BEST guard-valid price across bookmakers.
/// </summary>
public class FixtureOddsQuote
{
    public int Id { get; set; }
    public int FixtureId { get; set; }

    /// <summary>Bookmaker name as reported by API-Football.</summary>
    public string Bookmaker { get; set; } = "";

    /// <summary>Canonical market key (see OddsMarkets).</summary>
    public string Market { get; set; } = "";

    public double Price { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
