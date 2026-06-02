using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Ledgers;
using VetSystem.Domain.Common;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Ledgers;

/// <summary>
/// M16 per-farm charge routing (mirrors <c>InvoicesService.ResolveOwnerLedgerIdAsync</c>): farm
/// ledger when a <c>farm_id</c> is set, else the customer ledger, else <c>null</c> (walk-in). Runs
/// under the env-scoped query filter so it can only see the current environment's ledgers.
/// </summary>
public sealed class OwnerLedgerResolver : IOwnerLedgerResolver
{
    private readonly ApplicationDbContext _db;

    public OwnerLedgerResolver(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Guid?> ResolveAsync(Guid? customerId, Guid? farmId, CancellationToken cancellationToken)
    {
        if (farmId is { } fid)
        {
            return await _db.Ledgers.Where(l => l.FarmId == fid).Select(l => (Guid?)l.Id)
                       .FirstOrDefaultAsync(cancellationToken)
                   ?? throw new NotFoundException("ledger", fid);
        }

        if (customerId is { } cid)
        {
            return await _db.Ledgers.Where(l => l.CustomerId == cid).Select(l => (Guid?)l.Id)
                       .FirstOrDefaultAsync(cancellationToken)
                   ?? throw new NotFoundException("ledger", cid);
        }

        return null;
    }
}
