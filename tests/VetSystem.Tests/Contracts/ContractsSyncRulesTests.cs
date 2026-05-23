using FluentAssertions;

namespace VetSystem.Tests.Contracts;

/// <summary>
/// M8 task 13 — confirms <c>powersync/sync-rules.yaml</c> extends the <c>doctor_scope</c> bucket with
/// the contracts the doctor is responsible for, those contracts' medication-price overrides (via the
/// contract FK chain), and the doctor's batches — so a field doctor pulls their own contracts/batches
/// without leaking another doctor's rows (PRD §8.6).
/// </summary>
public sealed class ContractsSyncRulesTests
{
    [Fact]
    public void SyncRulesYaml_DoctorScope_IncludesContractsAndBatches()
    {
        var contents = File.ReadAllText(LocateSyncRulesFile());

        contents.Should().MatchRegex(@"FROM\s+contracts",
            "doctor_scope must sync the doctor's contracts");
        contents.Should().MatchRegex(@"FROM\s+contract_medication_prices",
            "doctor_scope must sync contract_medication_prices via the contract FK chain");
        contents.Should().MatchRegex(@"FROM\s+batches",
            "doctor_scope must sync the doctor's batches");

        contents.Should().Contain("contracts.responsible_doctor_id = bucket.doctor_id",
            "contracts are scoped to the responsible doctor");
        contents.Should().Contain("JOIN contracts ON contract_medication_prices.contract_id = contracts.id",
            "medication prices follow their parent contract's scope");
        contents.Should().Contain("batches.responsible_doctor_id = bucket.doctor_id",
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
