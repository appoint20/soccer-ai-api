namespace SoccerAi.Application.Entities;

/// <summary>
/// One reported absence for one fixture, as it stood when we fetched it.
/// </summary>
/// <remarks>
/// Insert-only and timestamped, for the same reason FixtureOddsQuote is: a
/// report captured before kickoff is pre-match knowledge, while the same
/// endpoint read afterwards returns confirmed absences that nobody could have
/// known when the board was published. Only <see cref="CapturedAtUtc"/>
/// separates the two, so it is recorded on every row rather than inferred later.
/// </remarks>
public sealed class FixtureInjury
{
    public int Id { get; set; }
    public int FixtureId { get; set; }
    public int TeamApiId { get; set; }
    public int PlayerApiId { get; set; }
    public string PlayerName { get; set; } = "";

    /// <summary>
    /// API-Football's classification, e.g. "Missing Fixture" or "Questionable".
    /// </summary>
    /// <remarks>
    /// Worth keeping distinct rather than collapsing to a boolean: a doubtful
    /// player is weaker evidence than a ruled-out one, and only the model should
    /// decide how much weaker.
    /// </remarks>
    public string Type { get; set; } = "";

    /// <summary>Free text, e.g. "Knee Injury", "Suspended".</summary>
    public string Reason { get; set; } = "";

    public DateTimeOffset CapturedAtUtc { get; set; }

    /// <summary>Kickoff, denormalised so a pre-match filter needs no join.</summary>
    public DateTimeOffset KickoffUtc { get; set; }

    /// <summary>True when this row was captured strictly before kickoff.</summary>
    public bool IsPreMatch => CapturedAtUtc < KickoffUtc;
}
