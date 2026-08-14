using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Primitives;
using SoccerAi.Api.Binding;

namespace soccer_ai_unit_tests.Api;

/// <summary>
/// The API serializes responses as snake_case but MVC binds query parameters by
/// property name, so every documented snake_case parameter used to bind to its
/// default and be ignored without a word in the logs. These tests pin the
/// translation that closed that gap.
/// </summary>
public class SnakeCaseQueryBindingTests
{
    private static SnakeCaseQueryValueProvider Provider(params (string Key, string Value)[] pairs)
    {
        // OrdinalIgnoreCase matches how ASP.NET builds the real request query
        // collection; with the default comparer these tests would assert against
        // a case-sensitive lookup the server never performs.
        var query = new QueryCollection(
            pairs.ToDictionary(
                p => p.Key,
                p => new StringValues(p.Value),
                StringComparer.OrdinalIgnoreCase));

        return new SnakeCaseQueryValueProvider(
            BindingSource.Query, query, CultureInfo.InvariantCulture);
    }

    [Theory]
    [InlineData("PageSize", "page_size", "20")]
    [InlineData("OnlyAnalyzed", "only_analyzed", "true")]
    [InlineData("DaysAhead", "days_ahead", "7")]
    [InlineData("UserMessage", "user_message", "hello")]
    public void Resolves_snake_case_key_for_pascal_case_property(
        string property, string queryKey, string value)
    {
        var provider = Provider((queryKey, value));

        provider.GetValue(property).FirstValue.Should().Be(value);
        provider.ContainsPrefix(property).Should().BeTrue();
    }

    /// <summary>
    /// The whole point of translating rather than replacing: callers already
    /// sending camelCase must not break on this deploy.
    /// </summary>
    [Fact]
    public void Still_resolves_original_casing()
    {
        var provider = Provider(("pageSize", "35"));

        provider.GetValue("PageSize").FirstValue.Should().Be("35");
    }

    [Fact]
    public void Prefers_exact_match_when_both_spellings_are_sent()
    {
        var provider = Provider(("pageSize", "10"), ("page_size", "99"));

        provider.GetValue("PageSize").FirstValue.Should().Be("10");
    }

    /// <summary>
    /// The binder probes "query.PageSize" before the bare name. Converting the
    /// whole string would mangle the prefix into a key matching nothing.
    /// </summary>
    [Fact]
    public void Converts_only_the_segment_after_the_last_dot()
    {
        var provider = Provider(("query.page_size", "15"));

        provider.GetValue("query.PageSize").FirstValue.Should().Be("15");
    }

    [Fact]
    public void Reports_no_value_for_an_absent_key()
    {
        var provider = Provider(("limit", "5"));

        provider.GetValue("Offset").FirstValue.Should().BeNull();
        provider.ContainsPrefix("Offset").Should().BeFalse();
    }

    [Fact]
    public void Single_word_properties_are_unaffected()
    {
        var provider = Provider(("limit", "25"), ("offset", "50"));

        provider.GetValue("Limit").FirstValue.Should().Be("25");
        provider.GetValue("Offset").FirstValue.Should().Be("50");
    }
}
