using FluentAssertions;

namespace VetSystem.Tests.Contracts;

/// <summary>
/// M8 task 13 / M14 / M15 / M29 — confirms <c>powersync/sync-rules.yaml</c> scopes contracts + batches
/// to the responsible doctor (<c>auth.user_id()</c> in the Sync Streams syntax) and the contracts'
/// farm coverage (M15) to its parent contract via the <c>by_contract</c> stream's <c>my_contracts</c>
/// CTE (PowerSync forbids JOINs) — so a field doctor pulls their own contracts/batches without leaking
/// another doctor's rows (PRD §8.6). M29 removed the per-medication price-override query.
/// </summary>
public sealed class ContractsSyncRulesTests
{
    [Fact]
    public void SyncRulesYaml_DoctorScope_IncludesContractsAndBatches()
    {
        var contents = File.ReadAllText(LocateSyncRulesFile());

        contents.Should().MatchRegex(@"FROM\s+contracts",
            "must sync the doctor's contracts");
        contents.Should().MatchRegex(@"FROM\s+contract_farms",
            "must sync the contract's farm coverage (M15)");
        contents.Should().MatchRegex(@"FROM\s+batches",
            "must sync the doctor's batches");

        // M29 — the per-medication price-override stream was removed; it must NOT reappear.
        contents.Should().NotMatchRegex(@"FROM\s+contract_medication_prices",
            "M29 removed per-contract medication pricing");

        contents.Should().MatchRegex(@"FROM\s+contracts\s+WHERE\s+responsible_doctor_id\s*=\s*auth\.user_id\(\)",
            "contracts are scoped to the responsible doctor");
        contents.Should().MatchRegex(@"FROM\s+contract_farms\s+WHERE\s+contract_id\s+IN\s+\(SELECT\s+contract_id\s+FROM\s+my_contracts\)",
            "farm coverage follows its parent contract via the by_contract stream (M15)");
        contents.Should().MatchRegex(@"FROM\s+batches\s+WHERE\s+responsible_doctor_id\s*=\s*auth\.user_id\(\)",
            "batches are scoped to the responsible doctor");
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
