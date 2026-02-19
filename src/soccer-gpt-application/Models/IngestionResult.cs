namespace soccer_gpt_application.Models;

/// <summary>
/// Result of an ingestion operation with detailed statistics
/// </summary>
public record IngestionResult
{
    public required int TotalProcessed { get; init; }
    public required int Saved { get; init; }
    public required int Skipped { get; init; }
    public required int CalculatedFromHistory { get; init; }
    public required List<SkippedFixture> SkippedDetails { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public TimeSpan Duration => EndTime - StartTime;
}

/// <summary>
/// Details about a skipped fixture
/// </summary>
public record SkippedFixture
{
    public required int ApiId { get; init; }
    public required string HomeTeam { get; init; }
    public required string AwayTeam { get; init; }
    public required DateTime Date { get; init; }
    public required string Reason { get; init; }
    public required List<string> MissingProperties { get; init; }
}
