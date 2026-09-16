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
    /// <summary>Exactly five rewrites of the five supplied facts, in order.</summary>
    public List<string> Checks { get; set; } = [];
}

public sealed record DecisionExplanationInput(
    int Version, int FixtureId, DateTimeOffset Kickoff, string HomeTeam, string AwayTeam,
    TeamStats HomeStats, TeamStats AwayStats, HeadToHeadModel? H2H,
    IReadOnlyList<DecisionExplanationMarket> Markets, IReadOnlyList<string> SelectedMarkets);

public sealed record DecisionExplanationMarket(
    string Market, string Selection, bool Qualified, string Gate, double Probability,
    double? Odds, string? Bookmaker, IReadOnlyList<string> Facts);

public sealed record DecisionPresentation(
    bool AiGenerated, IReadOnlyList<string> SummaryLines,
    IReadOnlyList<AiMarketExplanation> Markets);
