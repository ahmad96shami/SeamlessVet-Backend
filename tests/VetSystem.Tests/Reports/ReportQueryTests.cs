using FluentAssertions;
using VetSystem.Application.Reports;
using VetSystem.Domain.Common;

namespace VetSystem.Tests.Reports;

/// <summary>
/// M12 task 1 (unit) — the shared report-query primitives (<see cref="ReportQuery"/>). The half-open
/// window resolution and paging clamps back every report, so they are pinned here: unset sides span
/// all of time, the <c>to</c> day is fully included (end is exclusive midnight of the next day), an
/// inverted period is rejected, and paging clamps to <c>[1, 200]</c>.
/// </summary>
public sealed class ReportQueryTests
{
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly Jan31 = new(2026, 1, 31);

    [Fact]
    public void ResolveWindow_BothUnset_SpansAllOfTime()
    {
        var (start, end) = ReportQuery.ResolveWindow(null, null);

        start.Should().Be(DateTimeOffset.MinValue);
        end.Should().Be(DateTimeOffset.MaxValue);
    }

    [Fact]
    public void ResolveWindow_BothSet_IsHalfOpenAtMidnightUtc()
    {
        var (start, end) = ReportQuery.ResolveWindow(Jan1, Jan31);

        start.Should().Be(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        // End is exclusive midnight of the day AFTER `to`, so the whole `to` day is included.
        end.Should().Be(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ResolveWindow_IncludesLastInstantOfToDay()
    {
        var (start, end) = ReportQuery.ResolveWindow(Jan1, Jan31);
        var lastInstant = new DateTimeOffset(2026, 1, 31, 23, 59, 59, TimeSpan.Zero);

        (lastInstant >= start && lastInstant < end).Should().BeTrue();
    }

    [Fact]
    public void ResolveWindow_OnlyToSet_LowerBoundIsBeginningOfTime()
    {
        var (start, end) = ReportQuery.ResolveWindow(null, Jan31);

        start.Should().Be(DateTimeOffset.MinValue);
        end.Should().Be(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void ResolveWindow_OnlyFromSet_UpperBoundIsEndOfTime()
    {
        var (start, end) = ReportQuery.ResolveWindow(Jan1, null);

        start.Should().Be(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        end.Should().Be(DateTimeOffset.MaxValue);
    }

    [Fact]
    public void ResolveWindow_FromAfterTo_IsRejected()
    {
        var act = () => ReportQuery.ResolveWindow(Jan31, Jan1);

        act.Should().Throw<ConflictException>().Which.Code.Should().Be("invalid_period");
    }

    [Fact]
    public void ResolveWindow_SameDay_IsAllowed_AndCoversThatDay()
    {
        var (start, end) = ReportQuery.ResolveWindow(Jan1, Jan1);

        start.Should().Be(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        end.Should().Be(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(null, ReportQuery.DefaultPageSize)]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(75, 75)]
    [InlineData(1000, ReportQuery.MaxPageSize)]
    public void ClampTake_BoundsToOneThroughMax(int? take, int expected)
    {
        ReportQuery.ClampTake(take).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(-5, 0)]
    [InlineData(12, 12)]
    public void ClampSkip_IsNonNegative(int? skip, int expected)
    {
        ReportQuery.ClampSkip(skip).Should().Be(expected);
    }
}
