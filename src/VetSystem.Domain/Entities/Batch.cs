using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §5 — a Dawra/Cycle: a defined commercial animal-raising cycle with its own independent
/// accounting (PRD §7.2), the financial heart of the system. Carries the supervision-fee model and
/// the per-batch entitlement override that M9 reads to compute the responsible doctor's share.
/// Batch financial configuration is an Admin / Center-Web-App, online operation (PRD §7, §8.9), so
/// batches are server-authoritative on the sync path — the mobile device pulls them read-only.
/// </summary>
public sealed class Batch : Entity
{
    public Guid? ContractId { get; set; }

    public Guid CustomerId { get; set; }

    /// <summary>
    /// The farm this cycle runs on (M15). Nullable for backward compatibility; the data migration
    /// backfills it to the customer's default farm. <see cref="CustomerId"/> is retained as a
    /// denormalized mirror (<c>= farm.customer_id</c>).
    /// </summary>
    public Guid? FarmId { get; set; }

    public Guid ResponsibleDoctorId { get; set; }

    public int AnimalCount { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public string SupervisionFeeModel { get; set; } = FeeModel.FixedAmount;

    /// <summary>Amount, per-bird rate, or percent depending on <see cref="SupervisionFeeModel"/>.</summary>
    public decimal SupervisionFeeValue { get; set; }

    /// <summary>
    /// Null = inherit <c>system_settings.entitlement_enabled_global</c>; true/false = per-batch
    /// override (SCHEMA "Key invariants" #4). When effectively disabled, all profit goes to the clinic.
    /// </summary>
    public bool? EntitlementEnabled { get; set; }

    /// <summary>drug_profit | direct_fee — an accounting-only distinction in M28 (who funds the
    /// supervision fee): System A carves it from the clinic margin, System B charges the farmer on top.</summary>
    public string? EntitlementSystem { get; set; }

    public string Status { get; set; } = BatchStatus.Open;
}

/// <summary>Supervision / examination-fee model (PRD §7.3). The discriminated calculators land in M9.</summary>
public static class FeeModel
{
    public const string FixedAmount = "fixed_amount";
    public const string PercentOfInvoice = "percent_of_invoice";
    public const string PerBird = "per_bird";
    public const string PerBatchFixed = "per_batch_fixed";

    public static readonly IReadOnlyCollection<string> All = [FixedAmount, PercentOfInvoice, PerBird, PerBatchFixed];
}

/// <summary>Which entitlement mechanism a batch uses (PRD §7.4). Calculation lands in M9.</summary>
public static class EntitlementSystem
{
    public const string DrugProfit = "drug_profit";
    public const string DirectFee = "direct_fee";

    public static readonly IReadOnlyCollection<string> All = [DrugProfit, DirectFee];
}

public static class BatchStatus
{
    public const string Open = "open";
    public const string Closed = "closed";

    public static readonly IReadOnlyCollection<string> All = [Open, Closed];
}
