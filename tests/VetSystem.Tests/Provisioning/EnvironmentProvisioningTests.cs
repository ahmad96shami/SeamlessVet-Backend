using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Application.Common;
using VetSystem.Application.Provisioning;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Provisioning;

/// <summary>
/// M33 — the reusable provisioning path the platform console (M35) and the bootstrap seeder share.
/// Driven through the real DI host so the auditing interceptor mints ids / stamps audit columns,
/// exactly as it will in production.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EnvironmentProvisioningTests
{
    [Fact]
    public async Task ProvisionAsync_creates_a_full_environment_with_an_active_admin()
    {
        await using var factory = new VetApiFactory();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var password = "Provision_pw_1!";
        var request = new ProvisionEnvironmentRequest(
            CenterName: $"Center {suffix}",
            Code: $"PROV-{suffix}",
            Mode: EnvironmentMode.Solo,
            AdminFullName: "Center Owner",
            AdminPhone: $"+9705{suffix}",
            AdminPassword: password,
            AdminEmail: null);

        ProvisionEnvironmentResult result;
        using (var scope = factory.Services.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IEnvironmentProvisioningService>();
            result = await svc.ProvisionAsync(request, environmentId: null, CancellationToken.None);
        }

        try
        {
            await using var db = ReadContext(result.EnvironmentId);

            var env = await db.Environments.FirstAsync(e => e.Id == result.EnvironmentId);
            env.Status.Should().Be(EnvironmentStatus.Active);
            env.Code.Should().Be(request.Code);

            var roleKeys = await db.Roles.Select(r => r.Key).ToListAsync();
            roleKeys.Should().BeEquivalentTo(RoleKey.All);

            var permKeys = await db.Permissions.Select(p => p.Key).ToListAsync();
            permKeys.Should().BeEquivalentTo(PermissionKey.All);

            // The admin role carries every permission.
            var adminRoleId = await db.Roles.Where(r => r.Key == RoleKey.Admin).Select(r => r.Id).FirstAsync();
            var adminPermCount = await db.RolePermissions.CountAsync(rp => rp.RoleId == adminRoleId);
            adminPermCount.Should().Be(PermissionKey.All.Count);

            (await db.SystemSettings.AnyAsync()).Should().BeTrue();
            (await db.Warehouses.AnyAsync(w => w.Name == "Central")).Should().BeTrue();

            var serviceCategories = await db.Services.Select(s => s.Category).ToListAsync();
            serviceCategories.Should().Contain([ServiceCategories.Checkup, ServiceCategories.NightStay]);

            var admin = await db.Users.FirstAsync(u => u.Id == result.AdminUserId);
            admin.Status.Should().Be(UserStatus.Active);
            admin.RoleId.Should().Be(adminRoleId);

            var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
            hasher.Verify(password, admin.PasswordHash).Should().BeTrue("the first admin must be able to authenticate");
        }
        finally
        {
            await DeleteEnvAsync(result.EnvironmentId);
        }
    }

    [Fact]
    public async Task ProvisionAsync_rejects_a_duplicate_code()
    {
        await using var factory = new VetApiFactory();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var code = $"DUP-{suffix}";
        ProvisionEnvironmentRequest Make(string phone) => new(
            CenterName: $"Center {suffix}",
            Code: code,
            Mode: EnvironmentMode.Solo,
            AdminFullName: "Owner",
            AdminPhone: phone,
            AdminPassword: "Provision_pw_1!",
            AdminEmail: null);

        Guid firstEnvId = Guid.Empty;
        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                var svc = scope.ServiceProvider.GetRequiredService<IEnvironmentProvisioningService>();
                firstEnvId = (await svc.ProvisionAsync(Make($"+9705{suffix}1"), null, CancellationToken.None)).EnvironmentId;

                var act = async () => await svc.ProvisionAsync(Make($"+9705{suffix}2"), null, CancellationToken.None);
                (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("environment_code_taken");
            }

            await using var db = ReadContextAll();
            (await db.Environments.IgnoreQueryFilters().CountAsync(e => e.Code == code)).Should().Be(1);
        }
        finally
        {
            if (firstEnvId != Guid.Empty)
            {
                await DeleteEnvAsync(firstEnvId);
            }
        }
    }

    // ---- helpers ----

    private static ApplicationDbContext ReadContext(Guid environmentId)
        => new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(PgTestScope.ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options,
            new FakeCurrentUser { IsAuthenticated = true, UserId = Guid.NewGuid(), EnvironmentId = environmentId });

    private static ApplicationDbContext ReadContextAll()
        => new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(PgTestScope.ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options,
            new FakeCurrentUser());

    private static async Task DeleteEnvAsync(Guid environmentId)
    {
        await using var db = ReadContextAll();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM environments WHERE id = {environmentId};");
    }
}
