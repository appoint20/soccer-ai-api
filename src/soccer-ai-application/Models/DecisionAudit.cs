using System.Text.Json.Serialization;

namespace SoccerAi.Application.Models;

/// <summary>One rule evaluation: fired or not, with human-readable evidence.</summary>
public sealed record RuleResult(
    [property: JsonPropertyName("rule_id")] string RuleId,
    [property: JsonPropertyName("kind")] string Kind,      // "confirm" | "veto"
    [property: JsonPropertyName("fired")] bool Fired,
    [property: JsonPropertyName("evidence")] string Evidence)
{
    public const string Confirm = "confirm";
    public const string Veto = "veto";
}

/// <summary>Full rule-engine audit for one market — persisted in the snapshot.</summary>
public sealed record MarketRuleAudit(
    [property: JsonPropertyName("market")] string Market,
    [property: JsonPropertyName("probability")] double Probability,
    [property: JsonPropertyName("threshold")] double Threshold,
    [property: JsonPropertyName("probability_passed")] bool ProbabilityPassed,
    [property: JsonPropertyName("confirmations_fired")] int ConfirmationsFired,
    [property: JsonPropertyName("vetoes_fired")] int VetoesFired,
    [property: JsonPropertyName("qualified")] bool Qualified,
    [property: JsonPropertyName("rules")] IReadOnlyList<RuleResult> Rules)
{
    // ── Value gate (v3): EV-based qualification ──

    /// <summary>Guard-valid odds used for EV; null = analysis only.</summary>
    [JsonPropertyName("odds")] public double? Odds { get; init; }

    /// <summary>Minimum odds floor applied to this market.</summary>
    [JsonPropertyName("min_odds")] public double MinOdds { get; init; }

    /// <summary>EV = p × odds − 1 (null without valid odds).</summary>
    [JsonPropertyName("ev")] public double? Ev { get; init; }

    /// <summary>Per-market minimum edge required.</summary>
    [JsonPropertyName("min_edge")] public double MinEdge { get; init; }

    /// <summary>Fractional (quarter) Kelly stake as bankroll share (null unless qualified).</summary>
    [JsonPropertyName("kelly_stake")] public double? KellyStake { get; init; }

    /// <summary>Which gate stopped (or passed) this market — see GateOutcome.</summary>
    [JsonPropertyName("gate_outcome")] public string GateOutcome { get; init; } = "";

    [JsonIgnore]
    public IEnumerable<string> FiredConfirmRuleIds =>
        Rules.Where(r => r is { Kind: RuleResult.Confirm, Fired: true }).Select(r => r.RuleId);
}

/// <summary>Audit trail for the whole fixture (all markets).</summary>
public sealed record DecisionAudit(
    [property: JsonPropertyName("min_confirmations")] int MinConfirmationsRequired,
    [property: JsonPropertyName("markets")] IReadOnlyList<MarketRuleAudit> Markets,
    [property: JsonPropertyName("computed_at_utc")] DateTimeOffset ComputedAtUtc);
