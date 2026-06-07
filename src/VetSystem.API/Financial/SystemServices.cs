using Microsoft.EntityFrameworkCore;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Financial;

/// <summary>
/// M23 — the system services that let checkup-fee and night-stay invoice lines satisfy the
/// product-XOR-service CHECK on <c>invoice_items</c>. Find-or-created per environment so an admin
/// hard-delete can never break billing (<c>DataSeeder</c> seeds them for fresh environments,
/// this recreates them on demand). Resolve BEFORE opening the issuance transaction:
/// the concurrent-create race lands on <c>ux_services_system_category</c> and recovers by
/// re-querying, which would be impossible inside an aborted Postgres transaction.
/// </summary>
internal static class SystemServices
{
    public const string CheckupNameAr = "رسوم الكشف";
    public const string NightStayNameAr = "مبيت";

    public static Task<Service> GetOrCreateCheckupAsync(ApplicationDbContext db, CancellationToken cancellationToken)
        => GetOrCreateAsync(db, ServiceCategories.Checkup, CheckupNameAr, cancellationToken);

    public static Task<Service> GetOrCreateNightStayAsync(ApplicationDbContext db, CancellationToken cancellationToken)
        => GetOrCreateAsync(db, ServiceCategories.NightStay, NightStayNameAr, cancellationToken);

    private static async Task<Service> GetOrCreateAsync(
        ApplicationDbContext db, string category, string nameAr, CancellationToken cancellationToken)
    {
        // The global query filter scopes to the caller's environment; the auditing interceptor
        // stamps Id + EnvironmentId on insert.
        var existing = await db.Services.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Category == category, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var service = new Service { NameAr = nameAr, Category = category, DefaultPrice = 0m };
        db.Services.Add(service);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return service;
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Lost the create race — another request inserted it first; theirs wins.
            db.Entry(service).State = EntityState.Detached;
            return await db.Services.AsNoTracking()
                .FirstAsync(s => s.Category == category, cancellationToken);
        }
    }
}
