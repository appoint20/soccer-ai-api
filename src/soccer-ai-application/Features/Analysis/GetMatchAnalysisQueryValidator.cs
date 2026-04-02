using FluentValidation;

namespace SoccerAi.Application.Features.Analysis;

public class GetMatchAnalysisQueryValidator : AbstractValidator<GetMatchAnalysisQuery>
{
    public GetMatchAnalysisQueryValidator()
    {
        RuleFor(x => x.Language)
            .MaximumLength(5)
            .Matches("^[a-z]{2}$")
            .WithMessage("Language must be a 2-character ISO code (e.g., 'en', 'de')");
    }
}
