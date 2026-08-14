using FluentValidation;

namespace SoccerAi.Application.Models;

/// <summary>
/// A bare <c>limit</c>/<c>offset</c> window, for endpoints whose only input is
/// the page itself.
/// </summary>
public sealed class PageQuery : PageRequest;

public sealed class PageQueryValidator : AbstractValidator<PageQuery>
{
    public PageQueryValidator() => this.AddPagingRules();
}
