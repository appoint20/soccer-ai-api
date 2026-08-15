using System.Text.Json.Serialization;
using FluentValidation;
using Mediator.Net.Contracts;

namespace SoccerAi.Application.Features.Forecasts;

/// <summary>The head-to-head record over settled fixtures.</summary>
public sealed class GetForecastScoreboardQuery : IRequest
{
    /// <summary>Only score fixtures that kicked off on or after this date.</summary>
    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }
}

public sealed class GetForecastScoreboardQueryValidator : AbstractValidator<GetForecastScoreboardQuery>
{
    public GetForecastScoreboardQueryValidator()
    {
        RuleFor(q => q)
            .Must(q => q.From <= q.To)
            .When(q => q.From.HasValue && q.To.HasValue)
            .WithMessage("'from' must not be after 'to'.");
    }
}

/// <summary>
/// How one forecaster scored on a market. Both a Brier score and a hit rate are
/// reported because they answer different questions.
/// </summary>
public sealed record ForecastMarketScoreDto
{
    [JsonPropertyName("market")] public required string Market { get; init; }

    /// <summary>
    /// Mean squared error of the probability against the 0/1 outcome. Lower is
    /// better; 0.25 is what you get by saying 50% every time. This is the number
    /// that matters — it is a proper scoring rule, so the best score comes from
    /// reporting your true belief rather than from hedging or from being bold.
    /// </summary>
    [JsonPropertyName("brier_score")] public required double BrierScore { get; init; }

    /// <summary>
    /// Share of fixtures where rounding the probability at 0.5 matched the
    /// result. Easy to read, but it ignores confidence entirely: 0.51 and 0.99
    /// count the same. Use it for display, rank on Brier.
    /// </summary>
    [JsonPropertyName("hit_rate")] public required double HitRate { get; init; }

    /// <summary>Mean forecast probability — reveals a forecaster that hedges toward 0.5.</summary>
    [JsonPropertyName("mean_probability")] public required double MeanProbability { get; init; }

    /// <summary>Share of fixtures where the outcome actually happened.</summary>
    [JsonPropertyName("base_rate")] public required double BaseRate { get; init; }
}

/// <summary>One forecaster's full record.</summary>
public sealed record ForecasterScoreDto
{
    /// <summary>Model slug, or "system" for the statistical pipeline.</summary>
    [JsonPropertyName("forecaster")] public required string Forecaster { get; init; }

    [JsonPropertyName("settled_fixtures")] public required int SettledFixtures { get; init; }

    [JsonPropertyName("markets")] public required IReadOnlyList<ForecastMarketScoreDto> Markets { get; init; }

    /// <summary>
    /// Mean absolute error of expected goals against the real total. Null when
    /// the forecaster does not produce a goals estimate.
    /// </summary>
    [JsonPropertyName("goals_mae")] public required double? GoalsMae { get; init; }

    /// <summary>
    /// True while the sample is too small to read as a verdict. Forecast skill
    /// is high variance; a handful of fixtures separates nothing, and a
    /// scoreboard that ranks on ten results invites a conclusion the data
    /// cannot support.
    /// </summary>
    [JsonPropertyName("sample_too_small")] public required bool SampleTooSmall { get; init; }
}

public sealed record GetForecastScoreboardResponse : IResponse
{
    [JsonPropertyName("from")] public required DateOnly? From { get; init; }
    [JsonPropertyName("to")] public required DateOnly? To { get; init; }

    /// <summary>Settled fixtures with at least one model forecast.</summary>
    [JsonPropertyName("settled_fixtures")] public required int SettledFixtures { get; init; }

    /// <summary>The pipeline and every model, each scored on identical fixtures.</summary>
    [JsonPropertyName("forecasters")] public required IReadOnlyList<ForecasterScoreDto> Forecasters { get; init; }

    /// <summary>
    /// Lowest mean Brier score across markets, or null while every forecaster is
    /// still below the sample threshold. Deliberately null rather than a leader
    /// on thin data.
    /// </summary>
    [JsonPropertyName("leader")] public required string? Leader { get; init; }
}
