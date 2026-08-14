using FluentAssertions;
using SoccerAi.Application.Models;

namespace soccer_ai_unit_tests.Api;

/// <summary>
/// Paging is the contract every collection endpoint shares, and the default is
/// the part that matters: an omitted window must mean "first page", never
/// "everything", because unbounded reads are what this envelope exists to stop.
/// </summary>
public class PagingContractTests
{
    private sealed class Query : PageRequest;

    [Fact]
    public void Omitted_window_defaults_to_a_bounded_first_page()
    {
        var query = new Query();

        query.ResolveLimit().Should().Be(PageRequest.DefaultLimit);
        query.ResolveOffset().Should().Be(0);
    }

    [Fact]
    public void Limit_and_offset_are_used_as_given()
    {
        var query = new Query { Limit = 20, Offset = 40 };

        query.ResolveLimit().Should().Be(20);
        query.ResolveOffset().Should().Be(40);
    }

    [Theory]
    [InlineData(1, 20, 0)]
    [InlineData(2, 20, 20)]
    [InlineData(5, 10, 40)]
    public void Legacy_page_alias_resolves_to_the_same_window(int page, int pageSize, int expectedOffset)
    {
        var query = new Query { Page = page, PageSize = pageSize };

        query.ResolveLimit().Should().Be(pageSize);
        query.ResolveOffset().Should().Be(expectedOffset);
    }

    /// <summary>
    /// A caller mid-migration may send both. Canonical wins so the two spellings
    /// can never disagree about which rows come back.
    /// </summary>
    [Fact]
    public void Canonical_params_win_over_the_legacy_alias()
    {
        var query = new Query { Limit = 10, Offset = 5, Page = 9, PageSize = 100 };

        query.ResolveLimit().Should().Be(10);
        query.ResolveOffset().Should().Be(5);
    }

    [Fact]
    public void Limit_is_clamped_to_the_maximum()
    {
        new Query { Limit = 10_000 }.ResolveLimit().Should().Be(PageRequest.MaxLimit);
        new Query { Limit = 0 }.ResolveLimit().Should().Be(1);
    }

    [Fact]
    public void Page_zero_does_not_produce_a_negative_offset()
    {
        new Query { Page = 0, PageSize = 20 }.ResolveOffset().Should().Be(0);
    }

    [Fact]
    public void Has_more_is_false_on_the_final_short_page()
    {
        var page = PagedResponse<int>.From([1, 2, 3], limit: 50, offset: 80, total: 83);

        page.HasMore.Should().BeFalse();
    }

    [Fact]
    public void Has_more_is_true_while_rows_remain()
    {
        var page = PagedResponse<int>.From([1, 2, 3], limit: 3, offset: 0, total: 83);

        page.HasMore.Should().BeTrue();
    }

    [Fact]
    public void From_source_pages_an_in_memory_list_and_reports_the_full_total()
    {
        var page = PagedResponse<int>.FromSource([.. Enumerable.Range(1, 16)], limit: 5, offset: 10);

        page.Items.Should().Equal(11, 12, 13, 14, 15);
        page.Total.Should().Be(16);
        page.HasMore.Should().BeTrue();
    }

    [Fact]
    public void From_source_past_the_end_yields_an_empty_page_not_an_error()
    {
        var page = PagedResponse<int>.FromSource([.. Enumerable.Range(1, 4)], limit: 5, offset: 99);

        page.Items.Should().BeEmpty();
        page.Total.Should().Be(4);
        page.HasMore.Should().BeFalse();
    }
}
