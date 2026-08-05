using FluentValidation;
using Mediator.Net.Contracts;

namespace SoccerAi.Application.Features.Picks;

/// <summary>The day's sellable picks. Defaults to today (UTC).</summary>
public sealed class GetDailyPicksQuery : IRequest
{
    public DateOnly? Date { get; init; }
    public string? Language { get; init; }
}

public sealed class GetDailyPicksQueryValidator : AbstractValidator<GetDailyPicksQuery>
{
    /// <summary>
    /// Bounded so a stray date cannot trigger a precompute storm over fixtures
    /// nobody asked about.
    /// </summary>
    private const int MaxDaysFromToday = 30;

    public GetDailyPicksQueryValidator()
    {
        RuleFor(q => q.Date)
            .Must(BeWithinWindow)
            .When(q => q.Date.HasValue)
            .WithMessage($"Date must be within {MaxDaysFromToday} days of today.");

        RuleFor(q => q.Language)
            .Must(l => l is "en" or "de")
            .When(q => !string.IsNullOrWhiteSpace(q.Language))
            .WithMessage("Language must be 'en' or 'de'.");
    }

    private static bool BeWithinWindow(DateOnly? date)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var distance = Math.Abs(date!.Value.DayNumber - today.DayNumber);
        return distance <= MaxDaysFromToday;
    }
}
