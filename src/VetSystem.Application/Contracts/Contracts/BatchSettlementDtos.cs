namespace VetSystem.Application.Contracts.Contracts;

/// <summary>
/// M24 batch settlement (تصفية الدورة) payloads — SCHEMA §5a, invariant #11. The settle request
/// carries one negotiated price per product; products omitted keep their original resolution
/// (settlement → contract → invoice line, invariant #8). <c>DiscountAmount</c> is the batch-level
/// خصم: it reduces the farmer's debt AND the System-A doctor-share basis (client decision
/// 2026-06-07). Products only — service lines are never re-priced.
/// </summary>
public sealed record BatchSettlementRequest(
    Guid? Id,
    IReadOnlyList<BatchSettlementLineInput> Lines,
    decimal DiscountAmount,
    string? Notes,
    string IdempotencyKey);

public sealed record BatchSettlementLineInput(Guid ProductId, decimal SettledUnitPrice);

public sealed record BatchSettlementResponse(
    Guid Id,
    Guid BatchId,
    decimal RepricingDelta,
    decimal DiscountAmount,
    decimal OriginalTotal,
    decimal SettledTotal,
    decimal SupervisionFee,
    string? Notes,
    Guid SettledBy,
    DateTimeOffset SettledAt,
    IReadOnlyList<BatchSettlementLineResponse> Lines);

public sealed record BatchSettlementLineResponse(
    Guid ProductId,
    decimal SettledUnitPrice,
    decimal OriginalQuantity,
    decimal OriginalAmount,
    decimal Delta);

/// <summary>
/// Everything the settlement screen needs in one read: the batch's terms, its effective (non-void)
/// invoices, the per-product aggregation to re-price, the owner ledger position, and the guard flags
/// that would block settling. <c>ContractPrice</c> is a display hint — the active contract override
/// for the product as of today, when one applies.
/// </summary>
public sealed record BatchSettlementPreviewResponse(
    Guid BatchId,
    string BatchStatus,
    Guid CustomerId,
    string CustomerName,
    Guid? FarmId,
    string? FarmName,
    Guid ResponsibleDoctorId,
    string DoctorName,
    int AnimalCount,
    DateOnly StartDate,
    DateOnly? EndDate,
    string SupervisionFeeModel,
    decimal SupervisionFeeValue,
    bool? EntitlementEnabled,
    string? EntitlementSystem,
    decimal SupervisionFee,
    decimal OriginalTotal,
    Guid? LedgerId,
    decimal LedgerBalance,
    string LedgerStatus,
    bool AlreadySettled,
    DateTimeOffset? SettledAt,
    bool LedgerClosed,
    bool EntitlementFrozen,
    IReadOnlyList<SettlementPreviewProduct> Products,
    IReadOnlyList<SettlementPreviewInvoice> Invoices);

/// <summary>One re-priceable product aggregated across the batch's effective invoice lines.
/// <c>UnitPrices</c> lists the distinct billed prices (usually one); <c>WeightedAveragePrice</c>
/// is the delta-neutral prefill (settling at it yields a zero delta).</summary>
public sealed record SettlementPreviewProduct(
    Guid ProductId,
    string ProductName,
    decimal Quantity,
    IReadOnlyList<decimal> UnitPrices,
    decimal WeightedAveragePrice,
    decimal? ContractPrice,
    decimal OriginalAmount);

public sealed record SettlementPreviewInvoice(
    Guid InvoiceId,
    string? Number,
    string InvoiceType,
    DateTimeOffset IssuedAt,
    decimal Total);
