using FluentAssertions;
using AqtCoreFW.SharedKernel.Application.Pagination;

namespace AqtCoreFW.SharedKernel.Tests.Application.Pagination;

/// <summary>
/// Tests PagedResult record construction and metadata correctness.
/// TC-SK-A006, TC-SK-A007
/// </summary>
public sealed class PagedResultTests
{
    [Fact]
    public void PagedResult_Create_WithItems_HasCorrectMetadata()
    {
        // TC-SK-A006: PagedResult creates correct metadata (totalPages, hasNext, hasPrevious)
        var items = new[] { "a", "b", "c" };
        var meta = new PaginationMeta(Page: 1, PageSize: 10, TotalPages: 5, NextCursor: null, PrevCursor: null, HasNextPage: true);

        var result = new PagedResult<string>(items, TotalCount: 50, meta);

        result.Items.Should().HaveCount(3);
        result.TotalCount.Should().Be(50);
        result.Meta.Page.Should().Be(1);
        result.Meta.PageSize.Should().Be(10);
        result.Meta.TotalPages.Should().Be(5);
        result.Meta.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public void PagedResult_LastPage_HasNextPageFalse()
    {
        // TC-SK-A006: last page has no next page
        var meta = new PaginationMeta(Page: 5, PageSize: 10, TotalPages: 5, NextCursor: null, PrevCursor: null, HasNextPage: false);
        var result = new PagedResult<int>([], TotalCount: 50, meta);

        result.Meta.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public void PagedResult_EmptyResult_HasZeroItems()
    {
        // TC-SK-A007: PagedResult handles empty results
        var meta = new PaginationMeta(Page: 1, PageSize: 10, TotalPages: 0, NextCursor: null, PrevCursor: null, HasNextPage: false);
        var result = new PagedResult<string>([], TotalCount: 0, meta);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.Meta.TotalPages.Should().Be(0);
        result.Meta.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public void PagedResult_CursorPagination_ExposesNextAndPrevCursors()
    {
        var meta = new PaginationMeta(Page: null, PageSize: null, TotalPages: null,
            NextCursor: "next-token", PrevCursor: "prev-token", HasNextPage: true);
        var result = new PagedResult<string>(["x"], TotalCount: 1, meta);

        result.Meta.NextCursor.Should().Be("next-token");
        result.Meta.PrevCursor.Should().Be("prev-token");
    }
}
