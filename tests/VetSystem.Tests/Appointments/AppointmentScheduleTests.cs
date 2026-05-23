using FluentAssertions;
using VetSystem.Domain.Entities;

namespace VetSystem.Tests.Appointments;

/// <summary>
/// M6 task 10 — pure unit tests of the half-open conflict math
/// (<see cref="AppointmentSchedule.Overlaps"/> / <see cref="AppointmentSchedule.EndOf"/>). No
/// database. Intervals are expressed as minute offsets from a fixed base for readability.
/// </summary>
public sealed class AppointmentScheduleTests
{
    private static readonly DateTimeOffset Base = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int minutes) => Base.AddMinutes(minutes);

    [Theory]
    // back-to-back in both directions — touching boundaries do NOT overlap (half-open)
    [InlineData(0, 30, 30, 60, false)]
    [InlineData(30, 60, 0, 30, false)]
    // one-minute overlap either way
    [InlineData(0, 31, 30, 60, true)]
    [InlineData(30, 60, 0, 31, true)]
    // fully contained (and the reverse: one envelops the other)
    [InlineData(0, 60, 15, 45, true)]
    [InlineData(15, 45, 0, 60, true)]
    // identical windows
    [InlineData(0, 30, 0, 30, true)]
    // disjoint with a gap
    [InlineData(0, 30, 60, 90, false)]
    // shared start instant
    [InlineData(0, 30, 0, 10, true)]
    public void Overlaps_HalfOpenSemantics(int aStart, int aEnd, int bStart, int bEnd, bool expected)
        => AppointmentSchedule.Overlaps(At(aStart), At(aEnd), At(bStart), At(bEnd)).Should().Be(expected);

    [Fact]
    public void EndOf_UsesExplicitDuration()
        => AppointmentSchedule.EndOf(Base, 45).Should().Be(Base.AddMinutes(45));

    [Fact]
    public void EndOf_FallsBackToDefaultWhenDurationMissing()
        => AppointmentSchedule.EndOf(Base, null)
            .Should().Be(Base.AddMinutes(AppointmentSchedule.DefaultDurationMin));
}
