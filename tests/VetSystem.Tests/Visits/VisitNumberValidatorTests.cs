using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;
using VetSystem.Infrastructure.Visits;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Visits;

/// <summary>
/// M5 task 22 — visit-number rules (<see cref="VisitNumberValidator"/>): format, the prefix must
/// belong to the authenticated creator, uniqueness per environment, and — the offline-safety
/// property — two users with different prefixes never collide. DB-backed (PgTestScope).
/// </summary>
[Trait("Category", "Integration")]
public sealed class VisitNumberValidatorTests
{
    [Fact]
    public async Task Validate_EnforcesFormat_Prefix_AndUniqueness_AcrossTwoPrefixedUsers()
    {
        await using var scope = await PgTestScope.CreateAsync();
        await using var db = scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
        });

        var roleId = await SeedRoleAsync(db, scope.EnvironmentId);
        var userA = await SeedUserAsync(db, scope.EnvironmentId, roleId, "PFXA");
        var userB = await SeedUserAsync(db, scope.EnvironmentId, roleId, "PFXB");
        var customerId = await SeedCustomerAsync(db, scope.EnvironmentId);

        var validatorA = new VisitNumberValidator(db, AsUser(userA.Id, scope.EnvironmentId));
        var validatorB = new VisitNumberValidator(db, AsUser(userB.Id, scope.EnvironmentId));

        // Format: must be {prefix}-{digits}.
        await ShouldFailAsync(() => validatorA.ValidateAsync("PFXA", null, default), "invalid_visit_number");
        await ShouldFailAsync(() => validatorA.ValidateAsync("PFXA-abc", null, default), "invalid_visit_number");

        // Prefix must be the caller's own.
        await validatorA.ValidateAsync("PFXA-1", null, default); // ok
        await ShouldFailAsync(() => validatorA.ValidateAsync("PFXB-1", null, default), "visit_number_prefix_mismatch");

        // Uniqueness: once a number exists, re-using it is rejected (but the row may keep its own).
        var visitId = await SeedVisitAsync(db, scope.EnvironmentId, customerId, userA.Id, "PFXA-1");
        await ShouldFailAsync(() => validatorA.ValidateAsync("PFXA-1", null, default), "visit_number_taken");
        await validatorA.ValidateAsync("PFXA-1", excludeVisitId: visitId, default); // the row itself is fine

        // Offline-safety: different prefixes never collide, even with the same sequence number.
        await validatorA.ValidateAsync("PFXA-9", null, default);
        await validatorB.ValidateAsync("PFXB-9", null, default);
        await SeedVisitAsync(db, scope.EnvironmentId, customerId, userA.Id, "PFXA-9");
        await SeedVisitAsync(db, scope.EnvironmentId, customerId, userB.Id, "PFXB-9");
        var stored = db.Visits.IgnoreQueryFilters()
            .Count(v => v.EnvironmentId == scope.EnvironmentId && (v.VisitNumber == "PFXA-9" || v.VisitNumber == "PFXB-9"));
        stored.Should().Be(2, "same sequence under different prefixes coexist");
    }

    private static FakeCurrentUser AsUser(Guid userId, Guid envId) => new()
    {
        IsAuthenticated = true,
        UserId = userId,
        EnvironmentId = envId,
    };

    private static async Task ShouldFailAsync(Func<Task> act, string expectedCode)
    {
        var ex = await act.Should().ThrowAsync<ConflictException>();
        ex.Which.Code.Should().Be(expectedCode);
    }

    private static async Task<Guid> SeedRoleAsync(ApplicationDbContext db, Guid envId)
    {
        var now = DateTimeOffset.UtcNow;
        var role = new Role
        {
            Id = Guid.CreateVersion7(), EnvironmentId = envId, Key = RoleKey.VetClinic,
            Name = "r", CreatedAt = now, UpdatedAt = now,
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    private static async Task<User> SeedUserAsync(ApplicationDbContext db, Guid envId, Guid roleId, string prefix)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.CreateVersion7(), EnvironmentId = envId, RoleId = roleId,
            FullName = $"User {prefix}", PhonePrimary = $"+97{Guid.NewGuid().ToString("N")[..9]}",
            PasswordHash = "x", Status = UserStatus.Active, NumberPrefix = prefix,
            CreatedAt = now, UpdatedAt = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<Guid> SeedCustomerAsync(ApplicationDbContext db, Guid envId)
    {
        var now = DateTimeOffset.UtcNow;
        var customer = new Customer
        {
            Id = Guid.CreateVersion7(), EnvironmentId = envId, Type = CustomerType.Home,
            FullName = "Owner", CreatedAt = now, UpdatedAt = now,
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private static async Task<Guid> SeedVisitAsync(
        ApplicationDbContext db, Guid envId, Guid customerId, Guid doctorId, string visitNumber)
    {
        var now = DateTimeOffset.UtcNow;
        var visit = new Visit
        {
            Id = Guid.CreateVersion7(), EnvironmentId = envId, VisitType = VisitType.InClinic,
            VisitNumber = visitNumber, CustomerId = customerId, DoctorId = doctorId,
            Status = VisitStatus.Open, StartedAt = now, CreatedAt = now, UpdatedAt = now,
        };
        db.Visits.Add(visit);
        await db.SaveChangesAsync();
        return visit.Id;
    }
}
