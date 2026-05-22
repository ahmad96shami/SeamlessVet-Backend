using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VetSystem.Domain.Entities;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Identity;

[Trait("Category", "Integration")]
public sealed class EnvironmentIsolationTests
{
    [Fact]
    public async Task GlobalEnvFilter_PreventsReadAcrossEnvironments()
    {
        await using var envA = await PgTestScope.CreateAsync();
        await using var envB = await PgTestScope.CreateAsync();

        var userA = await CreateUserInAsync(envA);
        var userB = await CreateUserInAsync(envB);

        await using var dbA = envA.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            UserId = userA.Id,
            EnvironmentId = envA.EnvironmentId,
        });

        var visibleA = await dbA.Users.Select(u => u.Id).ToListAsync();
        visibleA.Should().Contain(userA.Id);
        visibleA.Should().NotContain(userB.Id,
            "env-scoped query filter must hide env B's rows from env A users");

        var fetchAttempt = await dbA.Users.FirstOrDefaultAsync(u => u.Id == userB.Id);
        fetchAttempt.Should().BeNull("non-IgnoreQueryFilters reads must respect the env filter");
    }

    private static async Task<User> CreateUserInAsync(PgTestScope scope)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });

        var role = new Role
        {
            EnvironmentId = scope.EnvironmentId,
            Key = RoleKey.VetField,
            Name = "Test",
        };
        db.Roles.Add(role);

        var user = new User
        {
            EnvironmentId = scope.EnvironmentId,
            RoleId = role.Id,
            FullName = "Iso Test",
            PhonePrimary = $"+97{Guid.NewGuid().ToString("N").Substring(0, 9)}",
            PasswordHash = "irrelevant",
            Status = UserStatus.Active,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
