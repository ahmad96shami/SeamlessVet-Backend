using FluentAssertions;

namespace VetSystem.Tests.Financial;

/// <summary>
/// M7 task 14 — confirms <c>powersync/sync-rules.yaml</c> extends the <c>doctor_scope</c> bucket
/// with the financial tables, filtered through the customer/visit FK chains so a field doctor pulls
/// their farms' invoices (and items, payments, vouchers) without leaking another doctor's rows.
/// </summary>
public sealed class FinancialSyncRulesTests
{
    [Fact]
    public void SyncRulesYaml_DoctorScope_IncludesFinancialTables()
    {
        var contents = File.ReadAllText(LocateSyncRulesFile());

        contents.Should().MatchRegex(@"FROM\s+invoices", "doctor_scope must sync invoices");
        contents.Should().MatchRegex(@"FROM\s+invoice_items", "doctor_scope must sync invoice_items via the invoice FK chain");
        contents.Should().MatchRegex(@"FROM\s+payments", "doctor_scope must sync payments via the invoice FK chain");
        contents.Should().MatchRegex(@"FROM\s+receipt_vouchers", "doctor_scope must sync receipt_vouchers via the customer FK chain");

        // Filtered through the same doctor chains as the rest of the bucket (no unscoped financial leak).
        contents.Should().Contain("JOIN invoices ON invoice_items.invoice_id = invoices.id");
        contents.Should().Contain("JOIN invoices ON payments.invoice_id = invoices.id");
        contents.Should().Contain("JOIN customers ON receipt_vouchers.customer_id = customers.id");
        contents.Should().Contain("JOIN visits ON invoices.visit_id = visits.id",
            "field invoices tied to the doctor's own visits must also reach the device");
    }

    private static string LocateSyncRulesFile()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "powersync", "sync-rules.yaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException("powersync/sync-rules.yaml not found above the test binary.");
    }
}
