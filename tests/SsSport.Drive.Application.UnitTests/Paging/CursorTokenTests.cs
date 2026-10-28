using System.Text;
using SsSport.Drive.Application.Paging;

namespace SsSport.Drive.Application.UnitTests.Paging;

/// <summary>
/// A cursor is the sort key and the id of the last item seen, packed as URL-safe text; anything else is rejected.
/// </summary>
public sealed class CursorTokenTests
{
    /// <summary>A time key and an id survive the round trip.</summary>
    [Fact]
    public void Time_key_and_id_round_trip()
    {
        var key = new DateTimeOffset(2026, 10, 25, 9, 30, 0, TimeSpan.Zero);
        var id = Guid.CreateVersion7();

        var token = CursorToken.Encode(key, id);

        CursorToken.TryDecode<DateTimeOffset>(token, out var decodedKey, out var decodedId).Should().BeTrue();
        decodedKey.Should().Be(key);
        decodedId.Should().Be(id);
    }

    /// <summary>A text key survives the round trip, and the token is URL-safe.</summary>
    [Fact]
    public void Text_key_round_trips_as_url_safe_text()
    {
        var id = Guid.CreateVersion7();

        var token = CursorToken.Encode("Ali Karimi/Ç?", id);

        token.Should().MatchRegex("^[A-Za-z0-9_-]+$");
        CursorToken.TryDecode<string>(token, out var key, out var decodedId).Should().BeTrue();
        key.Should().Be("Ali Karimi/Ç?");
        decodedId.Should().Be(id);
    }

    /// <summary>Text that is not a cursor is not well formed and does not decode.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not a cursor!")]
    [InlineData("bm90IGpzb24")]
    public void Garbage_is_rejected(string token)
    {
        CursorToken.IsWellFormed(token).Should().BeFalse();
        CursorToken.TryDecode<string>(token, out _, out _).Should().BeFalse();
    }

    /// <summary>JSON without both parts is rejected.</summary>
    [Fact]
    public void Json_without_an_id_is_rejected()
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"k":"a"}""")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        CursorToken.IsWellFormed(token).Should().BeFalse();
    }

    /// <summary>A cursor made for another sort key type does not decode.</summary>
    [Fact]
    public void Cursor_for_another_key_type_does_not_decode()
    {
        var token = CursorToken.Encode("Ali", Guid.CreateVersion7());

        CursorToken.IsWellFormed(token).Should().BeTrue();
        CursorToken.TryDecode<DateTimeOffset>(token, out _, out _).Should().BeFalse();
    }

    /// <summary>A date cursor is not read by a list sorted by text, although both are JSON strings.</summary>
    [Fact]
    public void Date_cursor_is_not_read_as_text()
    {
        var token = CursorToken.Encode(DateTimeOffset.UnixEpoch, Guid.CreateVersion7());

        CursorToken.TryDecode<string>(token, out _, out _).Should().BeFalse();
    }

    /// <summary>Oversized input is rejected before it is decoded.</summary>
    [Fact]
    public void Oversized_cursor_is_rejected()
    {
        CursorToken.IsWellFormed(new string('a', CursorToken.MaxLength + 1)).Should().BeFalse();
    }
}
