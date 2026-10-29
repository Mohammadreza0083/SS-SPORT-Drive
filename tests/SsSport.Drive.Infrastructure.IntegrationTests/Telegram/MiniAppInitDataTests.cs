using SsSport.Drive.Infrastructure.Telegram;

namespace SsSport.Drive.Infrastructure.IntegrationTests.Telegram;

/// <summary>
/// Mini App initData is trusted only when its hash matches Telegram's signature under our bot token and it was issued
/// within the last 24 hours (T-3).
/// </summary>
public sealed class MiniAppInitDataTests
{
    /// <summary>The bot token used for signing.</summary>
    private const string BotToken = "123456:ABC-test-token";

    /// <summary>
    /// initData signed with <see cref="BotToken"/> by an independent implementation of Telegram's algorithm (Python's
    /// <c>hmac</c>), issued at 2025-10-25 08:00:00 UTC (<c>auth_date=1761379200</c>).
    /// </summary>
    private const string KnownInitData =
        "query_id=AAHdF6IQAAAAAN0XohDhrOrc" +
        "&user=%7B%22id%22%3A424242%2C%22first_name%22%3A%22Reza%22%2C%22last_name%22%3A%22Ahmadi%22%2C%22username%22%3A%22reza_analyst%22%2C%22language_code%22%3A%22en%22%7D" +
        "&auth_date=1761379200" +
        "&hash=d77859cac2ce60e3f758431115520353389e8c5d2329ce37ed1fde7aacf74151";

    /// <summary>When <see cref="KnownInitData"/> was issued.</summary>
    private static readonly DateTimeOffset _issuedAt = DateTimeOffset.FromUnixTimeSeconds(1761379200);

    /// <summary>initData signed by Telegram's algorithm is accepted and yields the user.</summary>
    [Fact]
    public void Accepts_data_signed_with_the_bot_token()
    {
        var result = MiniAppInitData.Validate(KnownInitData, BotToken, _issuedAt.AddHours(1));

        result.IsValid.Should().BeTrue();
        result.Error.Should().BeNull();
        result.User.Should().Be(new MiniAppUser(424242, "Reza", "Ahmadi", "reza_analyst"));
        result.AuthDate.Should().Be(_issuedAt);
    }

    /// <summary>The signer used by the API tests agrees with the validator, extra fields included.</summary>
    [Fact]
    public void Extra_fields_are_part_of_the_signature()
    {
        var initData = InitDataSigner.Sign(BotToken, new Dictionary<string, string>
        {
            ["user"] = """{"id":7,"first_name":"Sara"}""",
            ["auth_date"] = _issuedAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["signature"] = "c2lnbmF0dXJl",
            ["chat_type"] = "private",
        });

        var result = MiniAppInitData.Validate(initData, BotToken, _issuedAt);

        result.IsValid.Should().BeTrue();
        result.User.Should().Be(new MiniAppUser(7, "Sara", null, null));
    }

    /// <summary>Changing any field (here: the user id) breaks the signature.</summary>
    [Fact]
    public void Rejects_tampered_data()
    {
        var tampered = KnownInitData.Replace("424242", "424243", StringComparison.Ordinal);

        MiniAppInitData.Validate(tampered, BotToken, _issuedAt).Error.Should().Be(MiniAppInitDataError.InvalidSignature);
    }

    /// <summary>Data signed for another bot is rejected.</summary>
    [Fact]
    public void Rejects_data_signed_by_another_bot()
    {
        MiniAppInitData.Validate(KnownInitData, "654321:other-token", _issuedAt).Error.Should().Be(MiniAppInitDataError.InvalidSignature);
    }

    /// <summary>Without a configured bot token nothing can be trusted.</summary>
    [Fact]
    public void Rejects_everything_without_a_bot_token()
    {
        MiniAppInitData.Validate(KnownInitData, string.Empty, _issuedAt).Error.Should().Be(MiniAppInitDataError.InvalidSignature);
    }

    /// <summary>A forged hash of the wrong shape is a signature failure, not a crash.</summary>
    [Theory]
    [InlineData("zz")]
    [InlineData("abc")]
    [InlineData("")]
    public void Rejects_a_malformed_hash(string hash)
    {
        var forged = KnownInitData[..KnownInitData.IndexOf("&hash=", StringComparison.Ordinal)] + "&hash=" + hash;

        MiniAppInitData.Validate(forged, BotToken, _issuedAt).Error.Should().Be(MiniAppInitDataError.InvalidSignature);
    }

    /// <summary>Data is valid for 24 hours after Telegram issued it.</summary>
    [Fact]
    public void Accepts_data_up_to_24_hours_old()
    {
        MiniAppInitData.Validate(KnownInitData, BotToken, _issuedAt.AddHours(24)).IsValid.Should().BeTrue();
    }

    /// <summary>Older data could have been stolen and is rejected.</summary>
    [Fact]
    public void Rejects_data_older_than_24_hours()
    {
        MiniAppInitData.Validate(KnownInitData, BotToken, _issuedAt.AddHours(24).AddSeconds(1)).Error.Should().Be(MiniAppInitDataError.Expired);
    }

    /// <summary>A few minutes of clock difference are tolerated; data from further in the future is not.</summary>
    [Fact]
    public void Rejects_data_from_the_future()
    {
        MiniAppInitData.Validate(KnownInitData, BotToken, _issuedAt.AddMinutes(-5)).IsValid.Should().BeTrue();
        MiniAppInitData.Validate(KnownInitData, BotToken, _issuedAt.AddMinutes(-6)).Error.Should().Be(MiniAppInitDataError.IssuedInFuture);
    }

    /// <summary>No initData at all.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_missing_data(string? initData)
    {
        MiniAppInitData.Validate(initData, BotToken, _issuedAt).Error.Should().Be(MiniAppInitDataError.Missing);
    }

    /// <summary>Data without a hash, or with a field twice, cannot be checked.</summary>
    [Theory]
    [InlineData("user=%7B%7D&auth_date=1761379200")]
    [InlineData("auth_date=1761379200&auth_date=1761379201&hash=00")]
    [InlineData("no-equals-sign")]
    public void Rejects_malformed_data(string initData)
    {
        MiniAppInitData.Validate(initData, BotToken, _issuedAt).Error.Should().Be(MiniAppInitDataError.Malformed);
    }

    /// <summary>Correctly signed data is still rejected when the user or the issue date is missing or unreadable.</summary>
    /// <param name="user">The user field, or <see langword="null"/> to leave it out.</param>
    /// <param name="authDate">The auth_date field, or <see langword="null"/> to leave it out.</param>
    [Theory]
    [InlineData(null, "1761379200")]
    [InlineData("not json", "1761379200")]
    [InlineData("""{"first_name":"No id"}""", "1761379200")]
    [InlineData("""{"id":0,"first_name":"Zero"}""", "1761379200")]
    [InlineData("""{"id":5,"first_name":"Sara"}""", null)]
    [InlineData("""{"id":5,"first_name":"Sara"}""", "yesterday")]
    public void Rejects_signed_data_without_a_readable_user_or_date(string? user, string? authDate)
    {
        var fields = new Dictionary<string, string> { ["query_id"] = "q1" };
        if (user is not null)
        {
            fields["user"] = user;
        }

        if (authDate is not null)
        {
            fields["auth_date"] = authDate;
        }

        MiniAppInitData.Validate(InitDataSigner.Sign(BotToken, fields), BotToken, _issuedAt).Error.Should().Be(MiniAppInitDataError.Malformed);
    }
}
