namespace SoccerAi.Application.Interfaces;

/// <summary>Outcome of the upstream calls made during one sync run.</summary>
public sealed record ApiCallStats(int Attempted, int Failed, string? LastError)
{
    public static ApiCallStats Empty { get; } = new(0, 0, null);

    public int Succeeded => Attempted - Failed;

    /// <summary>
    /// Every call the run made was rejected. This is the signature of a broken
    /// credential or a dead upstream, and is the one case that must not be
    /// reported as a successful sync — a run that writes nothing because
    /// nothing changed looks identical from the row counts alone.
    /// </summary>
    public bool AllFailed => Attempted > 0 && Failed == Attempted;
}

/// <summary>
/// Counts upstream API outcomes so a run can tell "nothing to update" apart from
/// "every request was rejected". Process-wide singleton; the worker runs one
/// pipeline at a time.
/// </summary>
public interface IApiCallTracker
{
    ApiCallStats Current { get; }

    void RecordSuccess();

    void RecordFailure(string reason);

    /// <summary>Clears the counters at the start of a run.</summary>
    void Reset();
}
