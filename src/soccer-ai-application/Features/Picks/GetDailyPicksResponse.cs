using System.Text.Json.Serialization;
using Mediator.Net.Contracts;

namespace SoccerAi.Application.Features.Picks;

/// <summary>One selection inside a ticket.</summary>
public sealed record PickLegDto
{
    [JsonPropertyName("fixture_id")] public required int FixtureId { get; init; }
    [JsonPropertyName("kickoff_utc")] public required DateTimeOffset KickoffUtc { get; init; }
    [JsonPropertyName("league")] public required string League { get; init; }
    [JsonPropertyName("match")] public required string Match { get; init; }
    [JsonPropertyName("market")] public required string Market { get; init; }
    [JsonPropertyName("selection")] public required string Selection { get; init; }
    [JsonPropertyName("probability")] public required double Probability { get; init; }

    /// <summary>
    /// The bookmaker's price, or null when none was published. Never a
    /// placeholder — a leg with no price is not a bet.
    /// </summary>
    [JsonPropertyName("odds")] public required double? Odds { get; init; }

    /// <summary>Expected value; null whenever <see cref="Odds"/> is.</summary>
    [JsonPropertyName("ev")] public required double? Ev { get; init; }
}

/// <summary>
/// A stakeable ticket: one leg, a same-match BTTS+Over 2.5 double, or an
/// accumulator.
/// </summary>
public sealed record TicketDto
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("legs")] public required IReadOnlyList<PickLegDto> Legs { get; init; }

    /// <summary>
    /// Whether every leg carries a real quote.
    /// </summary>
    /// <remarks>
    /// False means this is an analysis-only suggestion: the model rates the
    /// combination, but no bookmaker has priced it, so <c>total_odds</c>,
    /// <c>ev</c> and <c>kelly_stake</c> are all null and it must not be
    /// presented as a stakeable bet. <c>probability</c> and <c>fair_odds</c>
    /// remain meaningful — fair odds is the price it would need to be worth
    /// taking.
    /// </remarks>
    [JsonPropertyName("priced")] public required bool Priced { get; init; }

    /// <summary>Product of the leg prices; null when any leg is unpriced.</summary>
    [JsonPropertyName("total_odds")] public required double? TotalOdds { get; init; }

    /// <summary>
    /// Break-even price for this probability — never accept less. Derived from
    /// the model, so it is present even on an unpriced ticket.
    /// </summary>
    [JsonPropertyName("fair_odds")] public required double FairOdds { get; init; }

    [JsonPropertyName("probability")] public required double Probability { get; init; }

    /// <summary>Expected value; null without a price to compare against.</summary>
    [JsonPropertyName("ev")] public required double? Ev { get; init; }

    /// <summary>
    /// Quarter-Kelly stake as a share of bankroll, not a currency amount. Null
    /// on an unpriced ticket, because Kelly needs a price.
    /// </summary>
    [JsonPropertyName("kelly_stake")] public required double? KellyStake { get; init; }

    [JsonPropertyName("contains_goals_market")] public required bool ContainsGoalsMarket { get; init; }

    public static class Kinds
    {
        public const string Single = "single";
        public const string SameMatchPair = "same_match_pair";
        public const string Combo = "combo";
    }
}

/// <summary>Product 2 — highest-probability market on a fixture, priced or not.</summary>
public sealed record ConfidencePickDto
{
    [JsonPropertyName("fixture_id")] public required int FixtureId { get; init; }
    [JsonPropertyName("kickoff_utc")] public required DateTimeOffset KickoffUtc { get; init; }
    [JsonPropertyName("league")] public required string League { get; init; }
    [JsonPropertyName("match")] public required string Match { get; init; }
    [JsonPropertyName("market")] public required string Market { get; init; }
    [JsonPropertyName("selection")] public required string Selection { get; init; }

    /// <summary>
    /// Model probability. Upward biased by construction — it is the maximum of
    /// several market estimates, and a maximum sits above the average of what it
    /// was chosen from. Show the measured bucket hit rate from the backtest
    /// report to users, not this number.
    /// </summary>
    [JsonPropertyName("model_probability")] public required double ModelProbability { get; init; }
}

/// <summary>
/// How much of the day could actually be priced. An empty board with low
/// coverage means "no odds yet", not "no value found" — a distinction users and
/// operators both need.
/// </summary>
public sealed record PickCoverageDto
{
    [JsonPropertyName("fixtures")] public required int Fixtures { get; init; }
    [JsonPropertyName("analyzed")] public required int Analyzed { get; init; }
    [JsonPropertyName("priced")] public required int Priced { get; init; }
    [JsonPropertyName("priced_pct")] public required double PricedPct { get; init; }
}

public sealed record GetDailyPicksResponse : IResponse
{
    [JsonPropertyName("date")] public required DateOnly Date { get; init; }
    [JsonPropertyName("singles")] public required IReadOnlyList<TicketDto> Singles { get; init; }
    [JsonPropertyName("same_match_pairs")] public required IReadOnlyList<TicketDto> SameMatchPairs { get; init; }
    [JsonPropertyName("combos")] public required IReadOnlyList<TicketDto> Combos { get; init; }
    [JsonPropertyName("confidence_picks")] public required IReadOnlyList<ConfidencePickDto> ConfidencePicks { get; init; }
    [JsonPropertyName("coverage")] public required PickCoverageDto Coverage { get; init; }
}
