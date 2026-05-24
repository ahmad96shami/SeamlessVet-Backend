using FluentAssertions;

namespace VetSystem.Tests.Financial;

/// <summary>
/// M7 task 14 / M14 — confirms <c>powersync/sync-rules.yaml</c> scopes the financial tables to the
/// field doctor. PowerSync forbids JOINs, so invoices are reached through the <c>by_customer</c> /
/// <c>by_visit</c> parameter buckets and their children (invoice_items, payments) via the denormalized
/// <c>customer_id</c>/<c>visit_id</c> scope keys (M14_SyncScopeDenormalization); receipt_vouchers ride
/// the native customer_id. No JOIN may leak another doctor's rows.
/// </summary>
public sealed class FinancialSyncRulesTests
{
    [Fact]
    public void SyncRulesYaml_DoctorScope_IncludesFinancialTables()
    {
        var contents = File.ReadAllText(LocateSyncRulesFile());

        contents.Should().MatchRegex(@"FROM\s+invoices", "must sync invoices");
        contents.Should().MatchRegex(@"FROM\s+invoice_items", "must sync invoice_items");
        contents.Should().MatchRegex(@"FROM\s+payments", "must sync payments");
        contents.Should().MatchRegex(@"FROM\s+receipt_vouchers", "must sync receipt_vouchers");

        // Children reach their scope key directly (no JOIN) via both the customer and visit buckets.
        contents.Should().MatchRegex(@"FROM\s+invoice_items\s+WHERE\s+customer_id\s*=\s*bucket\.customer_id");
        contents.Should().MatchRegex(@"FROM\s+invoice_items\s+WHERE\s+visit_id\s*=\s*bucket\.visit_id");
        contents.Should().MatchRegex(@"FROM\s+payments\s+WHERE\s+customer_id\s*=\s*bucket\.customer_id");
        contents.Should().MatchRegex(@"FROM\s+payments\s+WHERE\s+visit_id\s*=\s*bucket\.visit_id");
        contents.Should().MatchRegex(@"FROM\s+receipt_vouchers\s+WHERE\s+customer_id\s*=\s*bucket\.customer_id");
        contents.Should().MatchRegex(@"FROM\s+invoices\s+WHERE\s+visit_id\s*=\s*bucket\.visit_id",
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
