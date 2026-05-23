using FluentAssertions;
using VetSystem.Domain.Entities;

namespace VetSystem.Tests.Appointments;

/// <summary>
/// M6 task 5 — pure unit tests of the appointment status state machine
/// (<see cref="AppointmentStatus.CanTransition"/> / <see cref="AppointmentStatus.IsTerminal"/>).
/// </summary>
public sealed class AppointmentStatusStateMachineTests
{
    [Theory]
    [InlineData(AppointmentStatus.Scheduled, AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Scheduled, AppointmentStatus.Attended)]
    [InlineData(AppointmentStatus.Scheduled, AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.Scheduled, AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.Attended)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.Cancelled)]
    public void CanTransition_AllowsForwardMoves(string from, string to)
        => AppointmentStatus.CanTransition(from, to).Should().BeTrue();

    [Theory]
    [InlineData(AppointmentStatus.Scheduled, AppointmentStatus.Scheduled)]   // same state is not a transition
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.Scheduled)]   // no going back to scheduled
    [InlineData(AppointmentStatus.Attended, AppointmentStatus.Cancelled)]    // terminal is final
    [InlineData(AppointmentStatus.Cancelled, AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.NoShow, AppointmentStatus.Attended)]
    [InlineData(AppointmentStatus.Scheduled, "archived")]                    // unknown target
    [InlineData("archived", AppointmentStatus.Scheduled)]                    // unknown source
    public void CanTransition_RejectsEverythingElse(string from, string to)
        => AppointmentStatus.CanTransition(from, to).Should().BeFalse();

    [Theory]
    [InlineData(AppointmentStatus.Attended, true)]
    [InlineData(AppointmentStatus.NoShow, true)]
    [InlineData(AppointmentStatus.Cancelled, true)]
    [InlineData(AppointmentStatus.Scheduled, false)]
    [InlineData(AppointmentStatus.Confirmed, false)]
    public void IsTerminal_OnlyForClosedStates(string status, bool expected)
        => AppointmentStatus.IsTerminal(status).Should().Be(expected);
}
