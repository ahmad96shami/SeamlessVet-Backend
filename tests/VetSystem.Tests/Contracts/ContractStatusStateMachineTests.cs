using FluentAssertions;
using VetSystem.Domain.Entities;

namespace VetSystem.Tests.Contracts;

/// <summary>
/// M8 — pure unit tests of the contract status state machine
/// (<see cref="ContractStatus.CanTransition"/> / <see cref="ContractStatus.IsServerAuthoritative"/>).
/// No database. The activation gate (permission + online) layers on top of this in the service.
/// </summary>
public sealed class ContractStatusStateMachineTests
{
    [Theory]
    [InlineData(ContractStatus.Draft, ContractStatus.Active)]
    [InlineData(ContractStatus.Draft, ContractStatus.Cancelled)]
    [InlineData(ContractStatus.Active, ContractStatus.Completed)]
    [InlineData(ContractStatus.Active, ContractStatus.Cancelled)]
    public void CanTransition_AllowsLegalMoves(string from, string to)
        => ContractStatus.CanTransition(from, to).Should().BeTrue();

    [Theory]
    [InlineData(ContractStatus.Draft, ContractStatus.Draft)]        // same state is not a transition
    [InlineData(ContractStatus.Active, ContractStatus.Active)]
    [InlineData(ContractStatus.Active, ContractStatus.Draft)]       // no going back
    [InlineData(ContractStatus.Draft, ContractStatus.Completed)]    // a draft is never completed directly
    [InlineData(ContractStatus.Completed, ContractStatus.Active)]   // terminal is final
    [InlineData(ContractStatus.Completed, ContractStatus.Cancelled)]
    [InlineData(ContractStatus.Cancelled, ContractStatus.Active)]
    [InlineData(ContractStatus.Cancelled, ContractStatus.Completed)]
    [InlineData(ContractStatus.Draft, "archived")]                  // unknown target
    [InlineData("archived", ContractStatus.Active)]                 // unknown source
    public void CanTransition_RejectsEverythingElse(string from, string to)
        => ContractStatus.CanTransition(from, to).Should().BeFalse();

    [Theory]
    [InlineData(ContractStatus.Active, true)]
    [InlineData(ContractStatus.Completed, true)]
    [InlineData(ContractStatus.Cancelled, true)]
    [InlineData(ContractStatus.Draft, false)]
    public void IsServerAuthoritative_OnlyOnceBinding(string status, bool expected)
        => ContractStatus.IsServerAuthoritative(status).Should().Be(expected);
}
