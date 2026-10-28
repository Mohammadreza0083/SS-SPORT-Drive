using SsSport.Drive.Application.Paging;

namespace SsSport.Drive.Application.UnitTests.Paging;

/// <summary>
/// Offset paging defaults to the first page of 20, allows at most 100 per page, and reports how many pages there are.
/// </summary>
public sealed class PageRequestTests
{
    /// <summary>Without values, a request asks for the first page of the default size.</summary>
    [Fact]
    public void Defaults_to_the_first_page_of_twenty()
    {
        var request = new PageRequest();

        request.Page.Should().Be(1);
        request.PageSize.Should().Be(20);
        PageRequest.DefaultPageSize.Should().Be(20);
        PageRequest.MaxPageSize.Should().Be(100);
    }

    /// <summary>The number of rows to skip follows from the page and its size.</summary>
    [Theory]
    [InlineData(1, 20, 0)]
    [InlineData(2, 20, 20)]
    [InlineData(3, 7, 14)]
    public void Skip_follows_from_page_and_size(int page, int pageSize, int skip)
    {
        new PageRequest(page, pageSize).Skip.Should().Be(skip);
    }

    /// <summary>The page count rounds up, and an empty list has no pages.</summary>
    [Theory]
    [InlineData(45, 20, 3)]
    [InlineData(40, 20, 2)]
    [InlineData(1, 20, 1)]
    [InlineData(0, 20, 0)]
    public void Total_pages_round_up(int totalCount, int pageSize, int totalPages)
    {
        var result = PagedResult<string>.Create([], new PageRequest(1, pageSize), totalCount);

        result.TotalPages.Should().Be(totalPages);
        result.TotalCount.Should().Be(totalCount);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(pageSize);
    }

    /// <summary>A cursor request starts at the beginning with the default size.</summary>
    [Fact]
    public void Cursor_request_defaults_to_the_start()
    {
        var request = new CursorRequest();

        request.Cursor.Should().BeNull();
        request.PageSize.Should().Be(20);
    }
}
