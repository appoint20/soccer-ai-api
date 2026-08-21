using System.Text.Json.Serialization;
using FluentValidation;
using Mediator.Net.Contracts;

namespace SoccerAi.Application.Features.Picks;

/// <summary>Realized results for published tickets over a date range.</summary>
public sealed class GetPickPerformanceQuery : IRequest
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

public sealed class GetPickPerformanceQueryValidator : AbstractValidator<GetPickPerformanceQuery>
{
    public GetPickPerformanceQueryValidator()
    {
        RuleFor(q => q)
            .Must(q => q.From <= q.To)
            .When(q => q.From.HasValue && q.To.HasValue)
            .WithMessage("'from' must not be after 'to'.");
    }
}

public sealed record PerformanceSliceDto
{
    [JsonPropertyName("key")] public required string Key { get; init; }
    [JsonPropertyName("settled")] public required int Settled { get; init; }
    [JsonPropertyName("won")] public required int Won { get; init; }
    [JsonPropertyName("pending")] public required int Pending { get; init; }
    [JsonPropertyName("voided")] public required int Voided { get; init; }
    [JsonPropertyName("hit_rate_pct")] public required double HitRatePct { get; init; }
    [JsonPropertyName("staked")] public required double Staked { get; init; }
    [JsonPropertyName("returned")] public required double Returned { get; init; }
    [JsonPropertyName("roi_pct")] public required double RoiPct { get; init; }

    /// <summary>
    /// True while the sample is too small to read as evidence. Betting results
    /// are high variance: at thirty settled tickets the confidence interval on
    /// ROI still spans profit and loss, so a headline number here would be
    /// noise dressed as a track record.
    /// </summary>
    [JsonPropertyName("sample_too_small")] public required bool SampleTooSmall { get; init; }
}

/// <summary>One ISO week of realized results, oldest first.</summary>
public sealed record WeeklyPerformanceDto
{
    /// <summary>ISO week label, e.g. "W31".</summary>
    [JsonPropertyName("label")] public required string Label { get; init; }

    /// <summary>
    /// Settled tickets in the week. Published so a caller can suppress a week
    /// whose sample is too thin to mean anything, the same judgement
    /// <see cref="PerformanceSliceDto.SampleTooSmall"/> makes for the totals.
    /// </summary>
    [JsonPropertyName("settled")] public required int Settled { get; init; }

    /// <summary>
    /// Signed profit in units of one stake — not currency. A reader staking 5
    /// and a reader staking 500 should read the same number here.
    /// </summary>
    [JsonPropertyName("profit_units")] public required double ProfitUnits { get; init; }
}

public sealed record GetPickPerformanceResponse : IResponse
{
    [JsonPropertyName("from")] public required DateOnly From { get; init; }
    [JsonPropertyName("to")] public required DateOnly To { get; init; }
    [JsonPropertyName("overall")] public required PerformanceSliceDto Overall { get; init; }
    [JsonPropertyName("by_kind")] public required IReadOnlyList<PerformanceSliceDto> ByKind { get; init; }
    [JsonPropertyName("by_market")] public required IReadOnlyList<PerformanceSliceDto> ByMarket { get; init; }

    /// <summary>
    /// Live settled results per week. These are realized outcomes at published
    /// prices, never the backtest's simulated weekly breakdown.
    /// </summary>
    [JsonPropertyName("by_week")] public required IReadOnlyList<WeeklyPerformanceDto> ByWeek { get; init; }
}
