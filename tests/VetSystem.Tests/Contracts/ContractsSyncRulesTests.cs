using FluentAssertions;

namespace VetSystem.Tests.Contracts;

/// <summary>
/// M8 task 13 / M14 — confirms <c>powersync/sync-rules.yaml</c> scopes contracts + batches to the
/// responsible doctor (the <c>doctor</c> bucket) and the contracts' medication-price overrides to their
/// parent contract via the <c>by_contract</c> parameter bucket (PowerSync forbids JOINs) — so a field
/// doctor pulls their own contracts/batches without leaking another doctor's rows (PRD §8.6).
/// </summary>
public sealed class ContractsSyncRulesTests
{
    [Fact]
    public void SyncRulesYaml_DoctorScope_IncludesContractsAndBatches()
    {
        var contents = File.ReadAllText(LocateSyncRulesFile());

        contents.Should().MatchRegex(@"FROM\s+contracts",
            "must sync the doctor's contracts");
        contents.Should().MatchRegex(@"FROM\s+contract_medication_prices",
            "must sync contract_medication_prices");
        contents.Should().MatchRegex(@"FROM\s+batches",
            "must sync the doctor's batches");

        contents.Should().MatchRegex(@"FROM\s+contracts\s+WHERE\s+responsible_doctor_id\s*=\s*bucket\.doctor_id",
            "contracts are scoped to the responsible doctor");
        contents.Should().MatchRegex(@"FROM\s+contract_medication_prices\s+WHERE\s+contract_id\s*=\s*bucket\.contract_id",
            "medication prices follow their parent contract via the by_contract bucket");
        contents.Should().MatchRegex(@"FROM\s+batches\s+WHERE\s+responsible_doctor_id\s*=\s*bucket\.doctor_id",
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
