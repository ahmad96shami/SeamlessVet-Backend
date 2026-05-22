using FluentAssertions;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Identity;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Identity;

[Trait("Category", "Integration")]
public sealed class NumberPrefixGeneratorTests
{
    [Fact]
    public async Task GenerateUniqueAsync_ProducesDistinctPrefixes_AcrossUsersInSameEnvironment()
    {
        await using var scope = await PgTestScope.CreateAsync();
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });

        var role = new Role
        {
            EnvironmentId = scope.EnvironmentId,
            Key = RoleKey.VetField,
            Name = "Test role",
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var generator = new NumberPrefixGenerator(db);

        var prefixes = new HashSet<string>();
        for (var i = 0; i < 25; i++)
        {
            var prefix = await generator.GenerateUniqueAsync(scope.EnvironmentId, CancellationToken.None);
            prefixes.Add(prefix).Should().BeTrue("prefix must be unique across calls");

            db.Users.Add(new User
            {
                EnvironmentId = scope.EnvironmentId,
                RoleId = role.Id,
                FullName = $"User {i}",
                PhonePrimary = $"+99{i:D9}",
                PasswordHash = "irrelevant",
                Status = UserStatus.Active,
                NumberPrefix = prefix,
            });
            await db.SaveChangesAsync();
        }

        prefixes.Should().AllSatisfy(p =>
            p.Should().MatchRegex("^[A-Z]{3,4}$", "generator emits 3- or 4-letter codes"));
    }
}
