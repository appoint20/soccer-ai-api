using FluentValidation;

namespace SoccerAi.Application.Models;

/// <summary>
/// Shared paging validation. Applied by each concrete query validator, because
/// the validation filter resolves validators by the argument's concrete type
/// and would never find one registered against the abstract base.
/// </summary>
public static class PageRequestRules
{
    /// <summary>
    /// Rejects nonsense windows loudly rather than clamping them, so a caller
    /// sending <c>limit=0</c> learns it is wrong instead of silently receiving a
    /// default page and drawing conclusions from it.
    /// </summary>
    public static void AddPagingRules<T>(this AbstractValidator<T> validator)
        where T : PageRequest
    {
        ArgumentNullException.ThrowIfNull(validator);

        validator.RuleFor(x => x.Limit)
            .InclusiveBetween(1, PageRequest.MaxLimit)
            .When(x => x.Limit.HasValue)
            .WithMessage($"'limit' must be between 1 and {PageRequest.MaxLimit}.");

        validator.RuleFor(x => x.Offset)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Offset.HasValue)
            .WithMessage("'offset' must not be negative.");

        validator.RuleFor(x => x.PageSize)
            .InclusiveBetween(1, PageRequest.MaxLimit)
            .When(x => x.PageSize.HasValue)
            .WithMessage($"'page_size' must be between 1 and {PageRequest.MaxLimit}.");

        validator.RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .When(x => x.Page.HasValue)
            .WithMessage("'page' is one-based and must be 1 or greater.");
    }
}
