using Microsoft.EntityFrameworkCore;
using VetSystem.Domain.Common;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Financial;

/// <summary>
/// "Billed" is derived state: an invoice item's <c>prescription_id</c> / <c>procedure_id</c>
/// back-link IS the record that the charge was billed (no flag to drift). Once referenced, the
/// clinical row backing an invoice line must stay intact — deleting it (or rewriting its money
/// fields) would orphan the back-link and let the visit's record disagree with the issued,
/// append-only invoice. Shared by the REST services and the <c>/sync</c> handlers so the rule
/// holds on every write path.
/// </summary>
internal static class BilledChargeGuard
{
    public static async Task EnsurePrescriptionNotBilledAsync(
        ApplicationDbContext db, Guid prescriptionId, CancellationToken cancellationToken)
    {
        if (await db.InvoiceItems.AsNoTracking()
                .AnyAsync(it => it.PrescriptionId == prescriptionId, cancellationToken))
        {
            throw new ConflictException("prescription_billed",
                $"Prescription '{prescriptionId}' is billed on an invoice and can no longer be removed.");
        }
    }

    public static async Task EnsureProcedureNotBilledAsync(
        ApplicationDbContext db, Guid procedureId, CancellationToken cancellationToken)
    {
        if (await db.InvoiceItems.AsNoTracking()
                .AnyAsync(it => it.ProcedureId == procedureId, cancellationToken))
        {
            throw new ConflictException("procedure_billed",
                $"Procedure '{procedureId}' is billed on an invoice and can no longer be removed or re-priced.");
        }
    }
}
