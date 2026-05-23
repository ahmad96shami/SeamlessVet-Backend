namespace VetSystem.Application.Financial;

/// <summary>
/// Validates a client-supplied <c>invoices.number</c> (SCHEMA "Key invariants" #9) — the same
/// per-user-prefixed, offline-safe scheme as <c>visit_number</c>. The server checks the format,
/// that the prefix belongs to the authenticated issuer (a device can't mint numbers under another
/// user's prefix), and that the number is unique per environment. The DB also enforces uniqueness
/// via <c>ux_invoices_env_number</c>; this validator gives a typed error before hitting that
/// constraint and is the single place invoice-number rules live (re-used by the issuance endpoints
/// and the sync handler). A null/blank number is allowed (server-side void rows carry no number).
/// </summary>
public interface IInvoiceNumberValidator
{
    /// <summary>
    /// Throws a typed domain error if <paramref name="number"/> is malformed, carries a prefix that
    /// isn't the current user's, or already exists in the environment. <paramref name="excludeInvoiceId"/>
    /// lets a row keep its own number.
    /// </summary>
    Task ValidateAsync(string number, Guid? excludeInvoiceId, CancellationToken cancellationToken);
}
