using Microsoft.EntityFrameworkCore;
using VetSystem.Domain.Common;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Financial;

/// <summary>
/// "Billed" is derived state: an invoice item's <c>prescription_id</c> / <c>procedure_id</c> /
/// <c>vaccination_id</c> back-link IS the record that the charge was billed (no flag to drift). Once referenced, the
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

    public static async Task EnsureVaccinationNotBilledAsync(
        ApplicationDbContext db, Guid vaccinationId, CancellationToken cancellationToken)
    {
        if (await db.InvoiceItems.AsNoTracking()
                .AnyAsync(it => it.VaccinationId == vaccinationId, cancellationToken))
        {
            throw new ConflictException("vaccination_billed",
                $"Vaccination '{vaccinationId}' is billed on an invoice and can no longer be removed or re-priced.");
        }
    }

    /// <summary>
    /// M23 — night stays have TWO possible billing writers: a POS invoice line (back-link) and the
    /// visit-completion ledger backstop (idempotency key <c>night-stay-{id}</c>). Either one freezes
    /// the stay's billing fields.
    /// </summary>
    public static async Task EnsureNightStayNotBilledAsync(
        ApplicationDbContext db, Guid nightStayId, CancellationToken cancellationToken)
    {
        var billed = await db.InvoiceItems.AsNoTracking()
            .AnyAsync(it => it.NightStayId == nightStayId, cancellationToken);
        if (!billed)
        {
            var key = $"night-stay-{nightStayId}";
            billed = await db.LedgerEntries.AsNoTracking()
                .AnyAsync(e => e.IdempotencyKey == key, cancellationToken);
        }

        if (billed)
        {
            throw new ConflictException("night_stay_billed",
                $"Night stay '{nightStayId}' is billed (invoice line or completion backstop) and can no longer be changed or removed.");
        }
    }

    /// <summary>
    /// M23 — same two-writer rule for the visit's checkup fee (invoice back-link or the
    /// <c>checkup-{visitId}</c> completion backstop). A billed fee can no longer be re-priced.
    /// </summary>
    public static async Task EnsureCheckupFeeNotBilledAsync(
        ApplicationDbContext db, Guid visitId, CancellationToken cancellationToken)
    {
        var billed = await db.InvoiceItems.AsNoTracking()
            .AnyAsync(it => it.CheckupFeeVisitId == visitId, cancellationToken);
        if (!billed)
        {
            var key = $"checkup-{visitId}";
            billed = await db.LedgerEntries.AsNoTracking()
                .AnyAsync(e => e.IdempotencyKey == key, cancellationToken);
        }

        if (billed)
        {
            throw new ConflictException("checkup_fee_billed",
                $"The checkup fee of visit '{visitId}' is billed (invoice line or completion backstop) and can no longer be changed.");
        }
    }
}
