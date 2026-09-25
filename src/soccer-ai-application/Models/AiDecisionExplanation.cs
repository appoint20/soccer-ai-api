using System.Text.Json.Serialization;
namespace SoccerAi.Application.Models;

/// <summary>Presentation only: never an input to probabilities or qualification.</summary>
public sealed class AiDecisionExplanation
{
    public int FixtureId { get; set; }
    public string InputHash { get; set; } = "";
    public string ModelVersion { get; set; } = "";
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public AiDecisionLanguage En { get; set; } = new();
    public AiDecisionLanguage De { get; set; } = new();
}

public sealed class AiDecisionLanguage
{
    public List<string> SummaryLines { get; set; } = [];
    public List<AiMarketExplanation> Markets { get; set; } = [];
}

public sealed class AiMarketExplanation
{
    public string Market { get; set; } = "";

    /// <summary>One rewrite per supplied fact, in the same order.</summary>
    public List<string> Checks { get; set; } = [];

    /// <summary>
    /// Whether each check above speaks for the market, against it, or neither
    /// — index-aligned with <see cref="Checks"/>, so the app can mark them.
    /// </summary>
    /// <remarks>
    /// Decided here, never by the writer. A model asked to rewrite a sentence
    /// may not invert its own tick: an evidence check that fired against a
    /// market has to keep reading as a mark against it however the words move.
    /// </remarks>
    [JsonPropertyName("check_outcomes")]
    public List<bool?> CheckOutcomes { get; set; } = [];
}

public sealed record DecisionExplanationInput(
    int Version, int FixtureId, DateTimeOffset Kickoff, string HomeTeam, string AwayTeam,
    TeamStats HomeStats, TeamStats AwayStats, HeadToHeadModel? H2H,
    IReadOnlyList<DecisionExplanationMarket> Markets, IReadOnlyList<string> SelectedMarkets);

public sealed record DecisionExplanationMarket(
    string Market, string Selection, bool Qualified, string Gate, double Probability,
    double? Odds, string? Bookmaker, IReadOnlyList<string> Facts)
{
    /// <summary>
    /// For each fact: true when it supports the market, false when it counts
    /// against it, null when it is context. Not sent to the writer — it is the
    /// server's own verdict and travels beside the rewritten words.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<bool?> Outcomes { get; init; } = [];
}

public sealed record DecisionPresentation(
    bool AiGenerated, IReadOnlyList<string> SummaryLines,
    IReadOnlyList<AiMarketExplanation> Markets);
