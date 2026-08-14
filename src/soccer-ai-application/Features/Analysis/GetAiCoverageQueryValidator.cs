using FluentValidation;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Features.Analysis;

public class GetAiCoverageQueryValidator : AbstractValidator<GetAiCoverageQuery>
{
    /// <summary>
    /// The scan reads every fixture in the window, so the window itself has to
    /// be bounded — paging the result would not stop an unbounded read.
    /// </summary>
    private const int MaxDaysAhead = 90;

    public GetAiCoverageQueryValidator()
    {
        this.AddPagingRules();

        RuleFor(x => x.DaysAhead)
            .InclusiveBetween(1, MaxDaysAhead)
            .WithMessage($"'days_ahead' must be between 1 and {MaxDaysAhead}.");
    }
}
