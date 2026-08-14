using System.Text.Json.Serialization;

namespace SoccerAi.Application.Models;

/// <summary>
/// The envelope every collection endpoint returns: the page, the window that
/// produced it, and the size of the full set.
///
/// <see cref="Total"/> counts the whole matching set, not the page, so a client
/// can render "showing 20 of 82" without a second request.
/// </summary>
public class PagedResponse<T>
{
    [JsonPropertyName("items")]
    public List<T> Items { get; init; } = [];

    [JsonPropertyName("limit")]
    public int Limit { get; init; }

    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    [JsonPropertyName("total")]
    public int Total { get; init; }

    /// <summary>
    /// Whether another page exists. Derived from the returned item count rather
    /// than from <see cref="Limit"/>, so a short final page reports correctly.
    /// </summary>
    [JsonPropertyName("has_more")]
    public bool HasMore => Offset + Items.Count < Total;

    public static PagedResponse<T> From(List<T> items, int limit, int offset, int total) => new()
    {
        Items = items,
        Limit = limit,
        Offset = offset,
        Total = total
    };

    /// <summary>
    /// Pages an already-materialized list. For collections assembled in memory
    /// and small by construction; anything backed by a table should page in the
    /// query instead.
    /// </summary>
    public static PagedResponse<T> FromSource(IReadOnlyList<T> source, int limit, int offset)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new PagedResponse<T>
        {
            Items = [.. source.Skip(offset).Take(limit)],
            Limit = limit,
            Offset = offset,
            Total = source.Count
        };
    }
}
