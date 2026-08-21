namespace SoccerAi.Application.Models;

/// <summary>
/// Final qualification decisions - the ONLY place for boolean flags and labels
/// </summary>
public sealed class QualificationDecisions
{
    public DrawDecision Draw { get; set; } = DrawDecision.NotQualified;
    public MarketDecision BTTS { get; set; } = MarketDecision.NotQualified;
    public MarketDecision Over25 { get; set; } = MarketDecision.NotQualified;
    public MarketDecision MatchWinner { get; set; } = MarketDecision.NotQualified;
    public MarketDecision TwoToThreeGoals { get; set; } = MarketDecision.NotQualified;
    public MarketDecision LowScoring { get; set; } = MarketDecision.NotQualified;
}

/// <summary>One piece of evidence behind a trap flag.</summary>
/// <param name="Id">Stable identifier, e.g. <c>market_favors_worse_side</c>.</param>
/// <param name="Evidence">Human-readable detail, safe to print verbatim.</param>
public sealed record TrapSignal(
    [property: System.Text.Json.Serialization.JsonPropertyName("id")] string Id,
    [property: System.Text.Json.Serialization.JsonPropertyName("evidence")] string Evidence);

/// <summary>
/// Whether the market looks like a trap, and why.
/// </summary>
/// <remarks>
/// A reason is always present when <see cref="IsTrap"/> is set. A warning a
/// reader cannot evaluate is close to useless, and inventing a rationale to
/// fill the gap would be worse — so when nothing was recorded, the reason says
/// exactly that rather than manufacturing one.
/// </remarks>
public sealed class TrapDecision
{
    // Named explicitly rather than left to the serializer's policy: the client
    // reads these keys, so they should not move if the policy is ever changed.
    [System.Text.Json.Serialization.JsonPropertyName("is_trap")]
    public bool IsTrap { get; init; }

    /// <summary>Never empty while <see cref="IsTrap"/> is true.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// The signals that fired, for rendering the warning as evidence rather
    /// than prose. May be empty; <see cref="Reason"/> stands on its own.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("signals")]
    public IReadOnlyList<TrapSignal> Signals { get; init; } = [];
}

public sealed class Qualification
{
    public bool IsQualified { get; set; }
    public double CombinedProbability { get; set; }
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// Draw qualification decision with detailed status
/// </summary>
public sealed class DrawDecision
{
    public bool IsQualified { get; init; }
    public bool IsStrongQualified { get; init; }
    public double Score { get; init; }
    public string Label { get; init; } = "Low Draw Likelihood";
    
    public static DrawDecision NotQualified => new()
    {
        IsQualified = false,
        IsStrongQualified = false,
        Score = 0,
        Label = "Low Draw Likelihood"
    };
    
    public static DrawDecision Create(double score) => new()
    {
        Score = Math.Round(score, 3),
        IsQualified = score >= 0.55,
        IsStrongQualified = score >= 0.70,
        Label = score switch
        {
            >= 0.70 => "Strong Draw Candidate",
            >= 0.55 => "Draw Candidate",
            >= 0.40 => "Possible Draw",
            _ => "Low Draw Likelihood"
        }
    };

    /// <summary>
    /// Create with explicit rejection reason (not qualified)
    /// </summary>
    public static DrawDecision Create(double score, string reason) => new()
    {
        Score = Math.Round(score, 3),
        IsQualified = false,
        IsStrongQualified = false,
        Label = reason
    };
}

/// <summary>
/// Market qualification decision (BTTS, Over2.5, etc.)
/// </summary>
public sealed class MarketDecision
{
    public bool IsQualified { get; set; }
    public double Confidence { get; init; }
    public bool Warning { get; init; }
    public string? WarningReason { get; init; }
    public string Reason { get; set; } = string.Empty;

    public static MarketDecision NotQualified => new() 
    { 
        IsQualified = false, 
        Confidence = 0,
        Reason = "Not predicted or insufficient data"
    };
    
    public static MarketDecision Create(double confidence, string? warningReason = null)
    {
        bool qualified = confidence >= 0.52 && string.IsNullOrWhiteSpace(warningReason);
        string reason = warningReason ?? string.Empty;
        
        if (!qualified && string.IsNullOrWhiteSpace(reason))
        {
             reason = $"Low confidence ({confidence:P0} < 60%)";
        }

        return new MarketDecision
        {
            Confidence = Math.Round(confidence, 3),
            IsQualified = qualified,
            Warning = !string.IsNullOrWhiteSpace(warningReason),
            WarningReason = warningReason,
            Reason = reason
        };
    }
}
