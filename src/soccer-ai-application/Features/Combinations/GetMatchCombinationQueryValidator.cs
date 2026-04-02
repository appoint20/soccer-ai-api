using FluentValidation;

namespace SoccerAi.Application.Features.Combinations;

public class GetMatchCombinationQueryValidator : AbstractValidator<GetMatchCombinationQuery>
{
    public GetMatchCombinationQueryValidator()
    {
        RuleFor(x => x.Date)
            .NotEmpty()
            .WithMessage("Date is required");

        RuleFor(x => x.Language)
            .MaximumLength(5)
            .Matches("^[a-z]{2}$")
            .WithMessage("Language must be a 2-character ISO code (e.g., 'en', 'de')");
    }
}
