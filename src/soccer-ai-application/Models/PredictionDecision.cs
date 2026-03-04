namespace SoccerAi.Application.Models;

/// <summary>
/// Final decision tier for a market prediction.
/// Used by the decision engine to classify each bet opportunity.
/// </summary>
public enum PredictionDecision
{
    /// <summary>No edge found — skip this bet.</summary>
    NoBet,

    /// <summary>Lean edge (EV 1-3%). Smallest stake / tracking only.</summary>
    LeanBet,

    /// <summary>Small edge detected (EV 3-8%). Low stake recommended.</summary>
    SmallEdge,

    /// <summary>Strong edge (EV > 8%) + model agrees with market. Full stake.</summary>
    StrongBet,

    /// <summary>Trap detected — avoid regardless of model output.</summary>
    Avoid
}
