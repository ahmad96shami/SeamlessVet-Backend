using FluentAssertions;
using VetSystem.Application.Partnership;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;

namespace VetSystem.Tests.Partnership;

/// <summary>
/// M10 task 9 (unit) — the per-environment ≤ 100% invariant (<see cref="IPartnershipValidator"/>) and
/// effective-date resolution at boundary days (the inclusive active-window rule, SCHEMA §1).
/// </summary>
public sealed class PartnershipShareLimitValidatorTests
{
    private readonly IPartnershipValidator _validator = new PartnershipShareLimitValidator();

    private static ShareWindow Window(string from, string? to, decimal pct) =>
        new(DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to), pct);

    [Fact]
    public void Empty_IsWithinLimit()
    {
        var act = () => _validator.EnsureWithinLimit([]);
        act.Should().NotThrow();
    }

    [Fact]
    public void ExactlyOneHundred_IsAllowed()
    {
        var shares = new[]
        {
            Window("2026-01-01", null, 40m),
            Window("2026-01-01", null, 35m),
            Window("2026-01-01", null, 25m),
        };

        var act = () => _validator.EnsureWithinLimit(shares);
        act.Should().NotThrow("40 + 35 + 25 = 100 is the boundary, not over it");
    }

    [Fact]
    public void OverHundredOnAnOverlappingDay_Throws()
    {
        var shares = new[]
        {
            Window("2026-01-01", null, 60m),
            Window("2026-03-01", null, 50m), // from Mar 1 the active total is 110
        };

        var act = () => _validator.EnsureWithinLimit(shares);
        act.Should().Throw<ConflictException>()
            .Which.Code.Should().Be("partnership_overallocated");
    }

    [Fact]
    public void SequentialNonOverlappingWindows_EachFull_IsAllowed()
    {
        // One window ends the day before the next begins → never more than 100% active at once.
        var shares = new[]
        {
            Window("2026-01-01", "2026-06-30", 100m),
            Window("2026-07-01", null, 100m),
        };

        var act = () => _validator.EnsureWithinLimit(shares);
        act.Should().NotThrow();
    }

    [Fact]
    public void EffectiveToIsInclusive_OverlapOnTheBoundaryDay_Throws()
    {
        // The first window is active THROUGH 2026-06-30; the second starts the same day → both active
        // on 2026-06-30, summing to 130. Proves effective_to is inclusive at the boundary.
        var shares = new[]
        {
            Window("2026-01-01", "2026-06-30", 70m),
            Window("2026-06-30", null, 60m),
        };

        var act = () => _validator.EnsureWithinLimit(shares);
        act.Should().Throw<ConflictException>().Which.Code.Should().Be("partnership_overallocated");
    }

    [Fact]
    public void EffectiveToBoundary_NoOverlap_NextDay_IsAllowed()
    {
        // First ends 2026-06-30, second starts 2026-07-01 — adjacent, never simultaneous.
        var shares = new[]
        {
            Window("2026-01-01", "2026-06-30", 70m),
            Window("2026-07-01", null, 60m),
        };

        var act = () => _validator.EnsureWithinLimit(shares);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("2026-01-01", false)] // day before from
    [InlineData("2026-01-02", true)]  // effective_from — inclusive
    [InlineData("2026-03-15", true)]  // inside
    [InlineData("2026-06-30", true)]  // effective_to — inclusive
    [InlineData("2026-07-01", false)] // day after to
    public void IsActiveOn_BoundaryDays_ResolveInclusively(string day, bool expected)
    {
        var share = new PartnershipShare
        {
            EffectiveFrom = new DateOnly(2026, 1, 2),
            EffectiveTo = new DateOnly(2026, 6, 30),
            SharePercent = 50m,
        };

        share.IsActiveOn(DateOnly.Parse(day)).Should().Be(expected);
    }

    [Fact]
    public void OpenEndedShare_IsActiveFarInTheFuture()
    {
        var share = new PartnershipShare { EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = null };
        share.IsActiveOn(new DateOnly(2099, 12, 31)).Should().BeTrue();
    }
}
