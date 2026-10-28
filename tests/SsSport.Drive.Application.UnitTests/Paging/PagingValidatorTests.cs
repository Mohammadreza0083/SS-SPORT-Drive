using SsSport.Drive.Application.Paging;

namespace SsSport.Drive.Application.UnitTests.Paging;

/// <summary>
/// Page numbers start at 1, page sizes are 1–100, and cursors must be well formed. Errors are keyed by the query
/// parameter names (<c>page</c>, <c>pageSize</c>, <c>cursor</c>) the client sent.
/// </summary>
public sealed class PagingValidatorTests
{
    /// <summary>The defaults and the limits are valid.</summary>
    [Theory]
    [InlineData(1, 20)]
    [InlineData(1, 1)]
    [InlineData(500, 100)]
    public void Valid_pages_pass(int page, int pageSize)
    {
        new PageRequestValidator().Validate(new PageRequest(page, pageSize)).IsValid.Should().BeTrue();
    }

    /// <summary>A page before the first is rejected under <c>page</c>.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Page_before_the_first_is_rejected(int page)
    {
        var result = new PageRequestValidator().Validate(new PageRequest(page, 20));

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("page");
    }

    /// <summary>A page size outside 1–100 is rejected under <c>pageSize</c>.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(10_000)]
    public void Page_size_outside_the_limits_is_rejected(int pageSize)
    {
        var result = new PageRequestValidator().Validate(new PageRequest(1, pageSize));

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("pageSize");
    }

    /// <summary>No cursor (the first page) and a real cursor are valid.</summary>
    [Fact]
    public void Missing_or_well_formed_cursor_passes()
    {
        var validator = new CursorRequestValidator();

        validator.Validate(new CursorRequest()).IsValid.Should().BeTrue();
        validator.Validate(new CursorRequest(CursorToken.Encode(42, Guid.CreateVersion7()))).IsValid.Should().BeTrue();
    }

    /// <summary>A tampered cursor is rejected under <c>cursor</c>.</summary>
    [Fact]
    public void Tampered_cursor_is_rejected()
    {
        var result = new CursorRequestValidator().Validate(new CursorRequest("tampered!"));

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("cursor");
    }

    /// <summary>A cursor page size outside 1–100 is rejected under <c>pageSize</c>.</summary>
    [Fact]
    public void Cursor_page_size_outside_the_limits_is_rejected()
    {
        var result = new CursorRequestValidator().Validate(new CursorRequest(null, 101));

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("pageSize");
    }
}
