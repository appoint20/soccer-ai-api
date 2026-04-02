using FluentValidation;

namespace SoccerAi.Application.Features.Backtesting;

public class GetBacktestReportQueryValidator : AbstractValidator<GetBacktestReportQuery>
{
    public GetBacktestReportQueryValidator()
    {
        RuleFor(x => x.WeeksBack)
            .GreaterThan(0).WithMessage("WeeksBack must be greater than 0")
            .LessThanOrEqualTo(52).WithMessage("WeeksBack must be at most 52");

        RuleFor(x => x.Stake)
            .GreaterThan(0).WithMessage("Stake must be greater than 0");
    }
}
