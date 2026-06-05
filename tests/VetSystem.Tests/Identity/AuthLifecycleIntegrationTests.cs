using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VetSystem.API.Identity;
using VetSystem.Application.Identity.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Identity;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Identity;

/// <summary>
/// M1 task 22 — register → approve → login → refresh → logout against the dev Postgres.
/// Drives <see cref="AuthService"/> + <see cref="UserAdminService"/> directly; HTTP layer is
/// exercised by the smoke tests in <c>vet-backend/CLAUDE.md</c> "Verifying ...".
/// </summary>
[Trait("Category", "Integration")]
public sealed class AuthLifecycleIntegrationTests
{
    [Fact]
    public async Task FullCycle_Register_Approve_Login_Refresh_Logout()
    {
        await using var scope = await PgTestScope.CreateAsync();

        // Seed the env with roles + permissions + an admin who will approve.
        await SeedRolesAndPermissionsAsync(scope);
        var admin = await SeedAdminAsync(scope);

        var hasher = new BCryptPasswordHasher();
        var tokenHasher = new Sha256RefreshTokenHasher();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "test",
            Audience = "test",
            SecretKey = "integration-test-secret-key-min-32-bytes-123",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7,
        });

        // --- Register (anonymous, in env scope) ---
        var anonUser = new FakeCurrentUser { EnvironmentId = scope.EnvironmentId };
        await using var anonDb = scope.CreateDbContext(anonUser);
        var anonAuth = new AuthService(
            anonDb,
            hasher,
            new JwtTokenService(jwtOptions, clock),
            new EfRefreshTokenStore(anonDb, tokenHasher, clock),
            clock,
            jwtOptions);

        var registered = await anonAuth.RegisterAsync(
            scope.EnvironmentId,
            new RegisterRequest(
                "Field Vet",
                "+970555111222",
                "vet@test.local",
                "Vet_pw_123!",
                RoleKey.VetField,
                null,
                null),
            CancellationToken.None);

        registered.UserId.Should().NotBeEmpty();
        registered.RegistrationRequestId.Should().NotBeEmpty();

        // --- Login while inactive must fail with account_inactive ---
        Func<Task> inactiveLogin = () => anonAuth.LoginAsync(
            scope.EnvironmentId,
            new LoginRequest("+970555111222", "Vet_pw_123!"),
            CancellationToken.None);

        (await inactiveLogin.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be("account_inactive");

        // --- Admin approves the registration ---
        var adminUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = admin.Id,
            Role = RoleKey.Admin,
        };
        await using var adminDb = scope.CreateDbContext(adminUser);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new PermissionResolver(adminDb, cache);
        var prefixes = new NumberPrefixGenerator(adminDb);
        var adminSvc = new UserAdminService(adminDb, prefixes, resolver, clock, adminUser, hasher);

        var approved = await adminSvc.ApproveAsync(
            registered.RegistrationRequestId,
            new ApproveRequest("approved by integration test"),
            CancellationToken.None);
        approved.Status.Should().Be(RequestStatus.Approved);

        // --- Login as approved user ---
        await using var userDb = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var userAuth = new AuthService(
            userDb,
            hasher,
            new JwtTokenService(jwtOptions, clock),
            new EfRefreshTokenStore(userDb, tokenHasher, clock),
            clock,
            jwtOptions);

        var pair = await userAuth.LoginAsync(
            scope.EnvironmentId,
            new LoginRequest("+970555111222", "Vet_pw_123!"),
            CancellationToken.None);

        pair.AccessToken.Should().NotBeNullOrEmpty();
        pair.RefreshToken.Should().NotBeNullOrEmpty();
        pair.RoleKey.Should().Be(RoleKey.VetField);

        // --- Refresh rotates the token ---
        var rotated = await userAuth.RefreshAsync(
            scope.EnvironmentId,
            pair.RefreshToken,
            CancellationToken.None);

        rotated.RefreshToken.Should().NotBe(pair.RefreshToken, "rotation must emit a fresh refresh token");
        rotated.AccessToken.Should().NotBe(pair.AccessToken);

        // --- Old refresh token is now invalid ---
        Func<Task> replay = () => userAuth.RefreshAsync(
            scope.EnvironmentId,
            pair.RefreshToken,
            CancellationToken.None);
        (await replay.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be("invalid_refresh_token");

        // --- Logout revokes the new refresh token ---
        await userAuth.LogoutAsync(scope.EnvironmentId, rotated.RefreshToken, CancellationToken.None);

        Func<Task> postLogout = () => userAuth.RefreshAsync(
            scope.EnvironmentId,
            rotated.RefreshToken,
            CancellationToken.None);
        (await postLogout.Should().ThrowAsync<ForbiddenException>())
            .Which.Code.Should().Be("invalid_refresh_token");
    }

    /// <summary>
    /// POST /admin/users — admin-created accounts skip the registration queue: active
    /// immediately (login works with no approval), prefix assigned, duplicate phone rejected,
    /// and field roles get their moving warehouse provisioned like an approval would.
    /// </summary>
    [Fact]
    public async Task AdminCreate_ActiveImmediately_CanLogin_FieldRoleGetsInventory()
    {
        await using var scope = await PgTestScope.CreateAsync();
        await SeedRolesAndPermissionsAsync(scope);
        var admin = await SeedAdminAsync(scope);

        var hasher = new BCryptPasswordHasher();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var adminUser = new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = admin.Id,
            Role = RoleKey.Admin,
        };
        await using var adminDb = scope.CreateDbContext(adminUser);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var adminSvc = new UserAdminService(
            adminDb,
            new NumberPrefixGenerator(adminDb),
            new PermissionResolver(adminDb, cache),
            clock,
            adminUser,
            hasher);

        // --- Create a cashier — active immediately with a prefix, no approval round-trip ---
        var cashier = await adminSvc.CreateUserAsync(
            new CreateUserRequest("Front Cashier", "+970555333444", null, "Cashier_pw_1!", RoleKey.Cashier, null, null),
            CancellationToken.None);

        cashier.Status.Should().Be(UserStatus.Active);
        cashier.RoleKey.Should().Be(RoleKey.Cashier);
        cashier.NumberPrefix.Should().NotBeNullOrEmpty();

        // --- The new account can log in straight away ---
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "test",
            Audience = "test",
            SecretKey = "integration-test-secret-key-min-32-bytes-123",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7,
        });
        await using var anonDb = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });
        var auth = new AuthService(
            anonDb,
            hasher,
            new JwtTokenService(jwtOptions, clock),
            new EfRefreshTokenStore(anonDb, new Sha256RefreshTokenHasher(), clock),
            clock,
            jwtOptions);

        var pair = await auth.LoginAsync(
            scope.EnvironmentId,
            new LoginRequest("+970555333444", "Cashier_pw_1!"),
            CancellationToken.None);
        pair.RoleKey.Should().Be(RoleKey.Cashier);

        // --- Duplicate phone is rejected ---
        Func<Task> dup = () => adminSvc.CreateUserAsync(
            new CreateUserRequest("Dup", "+970555333444", null, "Other_pw_123", RoleKey.Receptionist, null, null),
            CancellationToken.None);
        (await dup.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("phone_in_use");

        // --- A field role gets its moving warehouse provisioned in the same call ---
        var fieldVet = await adminSvc.CreateUserAsync(
            new CreateUserRequest("Created Field Vet", "+970555333445", null, "FieldVet_pw_1!", RoleKey.VetField, "LIC-9", null),
            CancellationToken.None);
        (await adminDb.FieldInventories.AnyAsync(f => f.DoctorId == fieldVet.Id)).Should().BeTrue();
    }

    private static async Task SeedRolesAndPermissionsAsync(PgTestScope scope)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser());

        foreach (var key in RoleKey.All)
        {
            db.Roles.Add(new Role
            {
                EnvironmentId = scope.EnvironmentId,
                Key = key,
                Name = key,
            });
        }

        foreach (var key in PermissionKey.All)
        {
            db.Permissions.Add(new Permission
            {
                EnvironmentId = scope.EnvironmentId,
                Key = key,
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task<User> SeedAdminAsync(PgTestScope scope)
    {
        await using var db = scope.CreateDbContext(new FakeCurrentUser { EnvironmentId = scope.EnvironmentId });

        var adminRole = db.Roles.Single(r => r.EnvironmentId == scope.EnvironmentId && r.Key == RoleKey.Admin);

        var hasher = new BCryptPasswordHasher();
        var admin = new User
        {
            EnvironmentId = scope.EnvironmentId,
            RoleId = adminRole.Id,
            FullName = "Integration Admin",
            PhonePrimary = $"+97{Guid.NewGuid().ToString("N").Substring(0, 9)}",
            PasswordHash = hasher.Hash("AdminTest_pw_1!"),
            Status = UserStatus.Active,
            NumberPrefix = "ITX",
        };
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        return admin;
    }
}
