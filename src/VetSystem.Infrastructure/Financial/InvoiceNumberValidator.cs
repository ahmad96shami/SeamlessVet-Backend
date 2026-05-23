using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Financial;
using VetSystem.Domain.Common;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Financial;

/// <inheritdoc cref="IInvoiceNumberValidator"/>
public sealed partial class InvoiceNumberValidator : IInvoiceNumberValidator
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public InvoiceNumberValidator(ApplicationDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public async Task ValidateAsync(string number, Guid? excludeInvoiceId, CancellationToken cancellationToken)
    {
        if (_user.UserId is not { } userId || _user.EnvironmentId is not { } envId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        if (string.IsNullOrWhiteSpace(number) || !NumberFormat().IsMatch(number))
        {
            throw new ConflictException("invalid_invoice_number",
                "invoice number must be '{prefix}-{sequence}', e.g. 'ADM-42'.");
        }

        var prefix = number[..number.IndexOf('-')];
        var ownPrefix = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.NumberPrefix)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrEmpty(ownPrefix) || !string.Equals(prefix, ownPrefix, StringComparison.Ordinal))
        {
            throw new ConflictException("invoice_number_prefix_mismatch",
                $"invoice number prefix '{prefix}' must match your assigned number prefix"
                + $" '{ownPrefix ?? "(none)"}'.");
        }

        // Match the ux_invoices_env_number constraint exactly: it spans soft-deleted rows (no
        // deleted_at in the index), so a retired number stays reserved. IgnoreQueryFilters to see them.
        var taken = await _db.Invoices
            .IgnoreQueryFilters()
            .AnyAsync(
                i => i.EnvironmentId == envId
                     && i.Number == number
                     && (excludeInvoiceId == null || i.Id != excludeInvoiceId),
                cancellationToken);

        if (taken)
        {
            throw new ConflictException("invoice_number_taken",
                $"invoice number '{number}' already exists in this environment.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9]+-[0-9]+$")]
    private static partial Regex NumberFormat();
}
