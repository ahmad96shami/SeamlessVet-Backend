using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using VetSystem.API.Jobs;
using VetSystem.Domain.Entities;

namespace VetSystem.Tests.Notifications;

/// <summary>
/// M11 task 4 — the Hangfire dashboard is reachable only by an authenticated admin. The decision
/// lives in a pure helper so it is unit-testable without standing up the dashboard.
/// </summary>
public sealed class HangfireDashboardAuthTests
{
    [Fact]
    public void Unauthenticated_principal_is_denied()
    {
        AdminOnlyDashboardAuthorizationFilter.IsAuthorized(new DefaultHttpContext()).Should().BeFalse();
    }

    [Theory]
    [InlineData(RoleKey.Admin, true)]
    [InlineData(RoleKey.Accountant, false)]
    [InlineData(RoleKey.Cashier, false)]
    [InlineData(RoleKey.VetField, false)]
    public void Only_authenticated_admin_is_authorized(string role, bool expected)
    {
        var context = new DefaultHttpContext { User = PrincipalWithRole(role) };

        AdminOnlyDashboardAuthorizationFilter.IsAuthorized(context).Should().Be(expected);
    }

    private static ClaimsPrincipal PrincipalWithRole(string role)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], authenticationType: "Test"));
}
