using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Partnership;
using VetSystem.Infrastructure.Persistence;
using VetSystem.Tests.Infrastructure;

namespace VetSystem.Tests.Partnership;

/// <summary>
/// M10 task 11 + exit criteria — three partners at 40 / 35 / 25 % split a closed batch's clinic share
/// according to the shares effective on the batch's close date. A superseded older window for the same
/// partner proves the distribution resolves by effective date, not just by partner.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ProfitDistributionIntegrationTests
{
    private static readonly DateOnly CloseDate = new(2026, 6, 30);

    [Fact]
    public async Task FortyThirtyFiveTwentyFive_SplitsClosedBatchClinicShare_OnTheCloseDate()
    {
        await using var scope = await PgTestScope.CreateAsync("partnership");
        var admin = await AdminTestSeed.SeedAdminAsync(scope); // gives roles + a usable user (the "doctor")

        var customerId = await SeedCustomerAsync(scope);
        var (partnerA, partnerB, partnerC) = await SeedPartnersAsync(scope);
        await SeedSharesAsync(scope, partnerA, partnerB, partnerC);
        var batchId = await SeedClosedBatchAsync(scope, customerId, admin.Id);

        await using var db = NewContext(scope, admin.Id);
        var batch = await db.Batches.AsNoTracking().FirstAsync(b => b.Id == batchId);
        batch.Status.Should().Be(BatchStatus.Closed);
        batch.EndDate.Should().Be(CloseDate);

        var svc = new ProfitDistributionService(db);

        // The clinic's share of this closed batch, distributed on the batch's close date.
        const decimal clinicShare = 1000m;
        var distribution = await svc.DistributeAsync(clinicShare, scope.EnvironmentId, batch.EndDate!.Value, default);

        distribution.Allocations.Should().HaveCount(3, "partner A's superseded 2025 window is not active on the close date");
        distribution.Allocations.Single(a => a.PartnerId == partnerA).Amount.Should().Be(400m);
        distribution.Allocations.Single(a => a.PartnerId == partnerB).Amount.Should().Be(350m);
        distribution.Allocations.Single(a => a.PartnerId == partnerC).Amount.Should().Be(250m);

        distribution.DistributedTotal.Should().Be(1000m);
        distribution.Retained.Should().Be(0m, "the active shares total 100% on the close date");
    }

    [Fact]
    public async Task ResolveShares_PicksTheWindowEffectiveOnTheGivenDate()
    {
        await using var scope = await PgTestScope.CreateAsync("partnership");
        await AdminTestSeed.SeedAdminAsync(scope);

        var (partnerA, partnerB, partnerC) = await SeedPartnersAsync(scope);
        await SeedSharesAsync(scope, partnerA, partnerB, partnerC);

        await using var db = NewContext(scope, Guid.Empty);
        var svc = new ProfitDistributionService(db);

        // On a 2025 date only partner A's old 99% window is active.
        var inThePast = await svc.ResolveSharesAsync(scope.EnvironmentId, new DateOnly(2025, 6, 1), default);
        inThePast.Should().ContainSingle();
        inThePast[0].PartnerId.Should().Be(partnerA);
        inThePast[0].SharePercent.Should().Be(99m);

        // On the batch close date the three current windows are active and the old one is gone.
        var onClose = await svc.ResolveSharesAsync(scope.EnvironmentId, CloseDate, default);
        onClose.Select(s => s.SharePercent).Should().BeEquivalentTo(new[] { 40m, 35m, 25m });
    }

    // ---- seeding ----

    private static async Task<Guid> SeedCustomerAsync(PgTestScope scope)
    {
        await using var db = NewContext(scope, Guid.Empty);
        var id = Guid.CreateVersion7();
        db.Customers.Add(new Customer
        {
            Id = id,
            EnvironmentId = scope.EnvironmentId,
            Type = CustomerType.PoultryFarm,
            FullName = "Distribution Farm",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<(Guid A, Guid B, Guid C)> SeedPartnersAsync(PgTestScope scope)
    {
        await using var db = NewContext(scope, Guid.Empty);
        Guid Add(string name)
        {
            var id = Guid.CreateVersion7();
            db.Partners.Add(new Partner { Id = id, EnvironmentId = scope.EnvironmentId, DisplayName = name });
            return id;
        }

        var a = Add("Partner A");
        var b = Add("Partner B");
        var c = Add("Partner C");
        await db.SaveChangesAsync();
        return (a, b, c);
    }

    private static async Task SeedSharesAsync(PgTestScope scope, Guid a, Guid b, Guid c)
    {
        await using var db = NewContext(scope, Guid.Empty);

        void AddShare(Guid partnerId, decimal pct, DateOnly from, DateOnly? to)
            => db.PartnershipShares.Add(new PartnershipShare
            {
                Id = Guid.CreateVersion7(),
                EnvironmentId = scope.EnvironmentId,
                PartnerId = partnerId,
                SharePercent = pct,
                EffectiveFrom = from,
                EffectiveTo = to,
            });

        // Partner A: a superseded 2025 window (99%, must NOT apply on the close date) and the current 40%.
        AddShare(a, 99m, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        AddShare(a, 40m, new DateOnly(2026, 1, 1), null);
        AddShare(b, 35m, new DateOnly(2026, 1, 1), null);
        AddShare(c, 25m, new DateOnly(2026, 1, 1), null);

        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedClosedBatchAsync(PgTestScope scope, Guid customerId, Guid doctorId)
    {
        await using var db = NewContext(scope, Guid.Empty);
        var id = Guid.CreateVersion7();
        db.Batches.Add(new Batch
        {
            Id = id,
            EnvironmentId = scope.EnvironmentId,
            CustomerId = customerId,
            ResponsibleDoctorId = doctorId,
            AnimalCount = 1000,
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = CloseDate,
            SupervisionFeeModel = FeeModel.FixedAmount,
            SupervisionFeeValue = 0m,
            Status = BatchStatus.Closed,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static ApplicationDbContext NewContext(PgTestScope scope, Guid userId)
        => scope.CreateDbContext(new FakeCurrentUser
        {
            IsAuthenticated = true,
            EnvironmentId = scope.EnvironmentId,
            UserId = userId,
        });
}
