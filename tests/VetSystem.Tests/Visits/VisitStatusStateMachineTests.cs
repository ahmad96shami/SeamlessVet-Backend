using FluentAssertions;
using VetSystem.Domain.Entities;

namespace VetSystem.Tests.Visits;

/// <summary>
/// M5 task 22 — pure unit tests of the visit status state machine
/// (<see cref="VisitStatus.CanTransition"/> / <see cref="VisitStatus.IsTerminal"/>). No database.
/// </summary>
public sealed class VisitStatusStateMachineTests
{
    [Theory]
    [InlineData(VisitStatus.Open, VisitStatus.InProgress)]
    [InlineData(VisitStatus.Open, VisitStatus.Completed)]
    [InlineData(VisitStatus.Open, VisitStatus.Cancelled)]
    [InlineData(VisitStatus.InProgress, VisitStatus.Completed)]
    [InlineData(VisitStatus.InProgress, VisitStatus.Cancelled)]
    public void CanTransition_AllowsForwardMoves(string from, string to)
        => VisitStatus.CanTransition(from, to).Should().BeTrue();

    [Theory]
    [InlineData(VisitStatus.Open, VisitStatus.Open)]               // same state is not a transition
    [InlineData(VisitStatus.InProgress, VisitStatus.InProgress)]
    [InlineData(VisitStatus.InProgress, VisitStatus.Open)]         // no going back
    [InlineData(VisitStatus.Completed, VisitStatus.InProgress)]    // terminal is final
    [InlineData(VisitStatus.Completed, VisitStatus.Cancelled)]
    [InlineData(VisitStatus.Cancelled, VisitStatus.Completed)]
    [InlineData(VisitStatus.Cancelled, VisitStatus.Open)]
    [InlineData(VisitStatus.Open, "archived")]                     // unknown target
    [InlineData("archived", VisitStatus.Open)]                     // unknown source
    public void CanTransition_RejectsEverythingElse(string from, string to)
        => VisitStatus.CanTransition(from, to).Should().BeFalse();

    [Theory]
    [InlineData(VisitStatus.Completed, true)]
    [InlineData(VisitStatus.Cancelled, true)]
    [InlineData(VisitStatus.Open, false)]
    [InlineData(VisitStatus.InProgress, false)]
    public void IsTerminal_OnlyForClosedStates(string status, bool expected)
        => VisitStatus.IsTerminal(status).Should().Be(expected);
}
