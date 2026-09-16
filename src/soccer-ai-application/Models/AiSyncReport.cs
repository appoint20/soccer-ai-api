namespace SoccerAi.Application.Models;

/// <summary>What one AI narrative run did, fixture by fixture.</summary>
/// <remarks>
/// On a date run the counts partition the day:
/// <c>FixturesOnDate = OutOfScope + NotUpcoming + Candidates</c>, and the
/// candidates split into <c>AlreadyAnalyzed + Failed + Generated</c>. A fixture
/// that fails while its inputs are prepared is never <see cref="Attempted"/>.
/// </remarks>
public sealed record AiSyncReport
{
    public DateTimeOffset WindowStartUtc { get; init; }
    public DateTimeOffset WindowEndUtc { get; init; }

    /// <summary>Every fixture on the requested date. Zero outside date runs.</summary>
    public int FixturesOnDate { get; init; }

    /// <summary>On the date, but in a league the sync does not cover.</summary>
    public int OutOfScope { get; init; }

    /// <summary>In scope, but already kicked off, finished, postponed or cancelled.</summary>
    public int NotUpcoming { get; init; }

    /// <summary>Upcoming, in-scope fixtures in the window.</summary>
    public int Candidates { get; init; }

    /// <summary>Candidates skipped because English and German text already exist.</summary>
    public int AlreadyAnalyzed { get; init; }

    /// <summary>Candidates prepared and sent to the language model.</summary>
    public int Attempted { get; init; }

    /// <summary>Fixtures whose narratives were saved.</summary>
    public int Generated { get; init; }

    public int Failed { get; init; }

    /// <summary>Fixtures to retry: preparation failed, or the model returned no usable text.</summary>
    public IReadOnlyList<int> FailedFixtureIds { get; init; } = [];
}
