using FluentAssertions;
using VetSystem.Application.Reports;
using VetSystem.Domain.Common;

namespace VetSystem.Tests.Reports;

/// <summary>
/// M12 task 16 — the keyset cursor primitive used by feed-like report lists (field-doctor-visits).
/// Pins the opaque token round-trip (instant + id preserved, URL-safe), the first-page (null) case,
/// malformed-token rejection, and the limit clamp — the contract every cursor-paged report relies on.
/// </summary>
public sealed class CursorPaginationTests
{
    [Fact]
    public void Encode_ThenDecode_RoundTripsInstantAndId()
    {
        // A non-UTC offset proves the token normalises to the instant, not wall-clock.
        var createdAt = new DateTimeOffset(2026, 3, 15, 9, 30, 0, TimeSpan.FromHours(2));
        var id = Guid.NewGuid();

        var token = CursorPagination.Encode(createdAt, id);
        var decoded = CursorPagination.Decode(token);

        decoded.Should().NotBeNull();
        decoded!.Value.Id.Should().Be(id);
        decoded.Value.CreatedAt.UtcTicks.Should().Be(createdAt.UtcTicks);
    }

    [Fact]
    public void Encode_ProducesUrlSafeToken()
    {
        var token = CursorPagination.Encode(DateTimeOffset.UtcNow, Guid.NewGuid());

        token.Should().NotContainAny("+", "/", "=");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Decode_BlankToken_IsFirstPage(string? token) =>
        CursorPagination.Decode(token).Should().BeNull();

    [Theory]
    [InlineData("not-valid-base64-@@@")]
    [InlineData("YWJj")] // base64 of "abc" — decodes but has no "ticks:id" separator
    public void Decode_MalformedToken_Throws_InvalidCursor(string token)
    {
        var act = () => CursorPagination.Decode(token);

        act.Should().Throw<ConflictException>().Which.Code.Should().Be("invalid_cursor");
    }

    [Theory]
    [InlineData(null, CursorPagination.DefaultPageSize)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(25, 25)]
    [InlineData(9999, CursorPagination.MaxPageSize)]
    public void ClampLimit_BoundsToValidRange(int? requested, int expected) =>
        CursorPagination.ClampLimit(requested).Should().Be(expected);
}
