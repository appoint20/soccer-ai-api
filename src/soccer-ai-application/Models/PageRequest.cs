namespace SoccerAi.Application.Models;

/// <summary>
/// Paging inputs shared by every collection endpoint.
///
/// <c>limit</c>/<c>offset</c> are canonical. <c>page</c>/<c>page_size</c> are
/// accepted as a deprecated alias because the published spec documented them
/// first and the mobile app was built against them; both resolve to the same
/// window, so a caller can migrate without a coordinated deploy.
/// </summary>
public abstract class PageRequest
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    /// <summary>
    /// Page size. <b>Omitting this does not disable paging</b> — it applies the
    /// default of 50 (maximum 200), which truncates a longer collection.
    /// </summary>
    /// <remarks>
    /// Stated explicitly because a silent default that drops rows is a footgun:
    /// a caller sending no paging parameters at all received the first 50
    /// fixtures of a day and no indication that more existed. Read
    /// <c>total</c> and <c>has_more</c> on the response, and page until
    /// <c>has_more</c> is false.
    /// </remarks>
    public int? Limit { get; set; }

    /// <summary>Zero-based row offset. Defaults to 0.</summary>
    public int? Offset { get; set; }

    /// <summary>
    /// One-based page number, an alias for <see cref="Offset"/>. Honoured:
    /// the offset applied is <c>(page - 1) × limit</c>.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Alias for <see cref="Limit"/>. Same default of 50 and maximum of 200.
    /// </summary>
    public int? PageSize { get; set; }

    /// <summary>
    /// Always a bounded number. An omitted limit means <see cref="DefaultLimit"/>,
    /// never "everything for that day" — an unpaged fixture list is precisely the
    /// slow path this envelope exists to close, so the default cannot be unbounded.
    /// </summary>
    /// <remarks>
    /// A method rather than a property so it is neither model-bound as a query
    /// parameter nor advertised as one in the OpenAPI document.
    /// </remarks>
    public int ResolveLimit() =>
        Math.Clamp(Limit ?? PageSize ?? DefaultLimit, 1, MaxLimit);

    /// <summary>
    /// Resolves <c>offset</c>, falling back to the one-based <c>page</c> alias.
    /// </summary>
    public int ResolveOffset() =>
        Offset ?? (Page.HasValue ? Math.Max(Page.Value - 1, 0) * ResolveLimit() : 0);
}
