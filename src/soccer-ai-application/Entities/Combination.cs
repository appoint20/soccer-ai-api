namespace SoccerAi.Application.Entities;

/// <summary>
/// Represents a combination/parlay manually created by the user for backtesting.
/// </summary>
public class Combination
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = "Pending";
    public double TotalOdds { get; set; }

    // --- Caching Support ---
    public DateTimeOffset? Date { get; set; }
    public string? Language { get; set; }
    public string? Payload { get; set; }
    public bool IsDailyCache { get; set; }
}
