using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace SoccerAi.Api.Binding;

/// <summary>
/// Binds snake_case query-string keys onto PascalCase properties, so
/// <c>?only_analyzed=true&amp;page_size=20</c> actually reaches the query object.
///
/// The API serializes every response through
/// <see cref="JsonNamingPolicy.SnakeCaseLower"/>, but that policy governs
/// System.Text.Json alone. MVC's model binder matches property names
/// case-insensitively and is blind to the underscore, so a documented
/// snake_case parameter silently bound to its default and the request ran as
/// though the caller had never sent it. That is how <c>only_analyzed</c> and
/// <c>page_size</c> came to be published in the spec yet have no effect.
///
/// Original casing still resolves first, so <c>?pageSize=20</c> keeps working
/// and no existing caller changes behaviour.
/// </summary>
public sealed class SnakeCaseQueryValueProvider(
    BindingSource bindingSource,
    IQueryCollection values,
    CultureInfo culture)
    : QueryStringValueProvider(bindingSource, values, culture)
{
    public override bool ContainsPrefix(string prefix)
    {
        if (base.ContainsPrefix(prefix))
            return true;

        var snake = ToSnakeCase(prefix);
        return !string.Equals(snake, prefix, StringComparison.Ordinal)
               && base.ContainsPrefix(snake);
    }

    public override ValueProviderResult GetValue(string key)
    {
        var result = base.GetValue(key);
        if (result != ValueProviderResult.None)
            return result;

        var snake = ToSnakeCase(key);
        return string.Equals(snake, key, StringComparison.Ordinal)
            ? result
            : base.GetValue(snake);
    }

    /// <summary>
    /// Converts the trailing segment only. The binder probes prefixed keys such
    /// as <c>query.PageSize</c> before the bare <c>PageSize</c>; converting the
    /// whole string would mangle the prefix and produce a key that matches
    /// nothing.
    /// </summary>
    private static string ToSnakeCase(string key)
    {
        if (key.Length == 0 || key.Contains('_', StringComparison.Ordinal))
            return key;

        var split = key.LastIndexOf('.') + 1;
        var head = key[..split];
        var tail = key[split..];

        return tail.Length == 0
            ? key
            : head + JsonNamingPolicy.SnakeCaseLower.ConvertName(tail);
    }
}

/// <summary>
/// Replaces the built-in query-string value provider with
/// <see cref="SnakeCaseQueryValueProvider"/>.
/// </summary>
public sealed class SnakeCaseQueryValueProviderFactory : IValueProviderFactory
{
    public Task CreateValueProviderAsync(ValueProviderFactoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ValueProviders.Add(new SnakeCaseQueryValueProvider(
            BindingSource.Query,
            context.ActionContext.HttpContext.Request.Query,
            CultureInfo.InvariantCulture));

        return Task.CompletedTask;
    }
}
