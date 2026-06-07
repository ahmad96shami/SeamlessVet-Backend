using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Contracts;
using VetSystem.Application.Financial;
using VetSystem.Application.Financial.Contracts;
using VetSystem.Application.Inventory;
using VetSystem.Application.Inventory.Contracts;
using VetSystem.Application.Ledgers;
using VetSystem.Application.Ledgers.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Financial;

/// <summary>
/// Invoice issuance + reads (M7 tasks 3–8, 12). All three issuance flows (POS, field, exam-fee)
/// funnel through one transactional core so the append-only invariants hold uniformly: an issued
/// invoice, its items + payments, the <c>sale_deduct</c> movements for product lines, and the
/// customer ledger posting all commit together or not at all. <c>cost_price</c> is snapshotted from
/// the product at sale time (SCHEMA invariant #8); a walk-in (null customer) skips the ledger
/// (PRD §5.4); the ledger entry records the credit/outstanding portion (total minus non-credit
/// payments). When an invoice is tied to a visit, that visit's unbilled dispensed meds and
/// procedures are auto-assembled as lines (task 8).
/// </summary>
public sealed class InvoicesService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;
    private readonly IClock _clock;
    private readonly IInvoiceNumberValidator _invoiceNumbers;
    private readonly IInventoryService _inventory;
    private readonly ILedgerService _ledgers;
    private readonly IPricingService _pricing;

    public InvoicesService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IMapper mapper,
        IClock clock,
        IInvoiceNumberValidator invoiceNumbers,
        IInventoryService inventory,
        ILedgerService ledgers,
        IPricingService pricing)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
        _clock = clock;
        _invoiceNumbers = invoiceNumbers;
        _inventory = inventory;
        _ledgers = ledgers;
        _pricing = pricing;
    }

    public async Task<InvoiceResponse> IssuePosAsync(PosInvoiceRequest request, CancellationToken cancellationToken)
    {
        var visit = await ResolveVisitAsync(request.VisitId, cancellationToken);

        // POS deducts product lines from the environment's central warehouse.
        var deduction = await ResolveWarehouseLocationAsync(cancellationToken);

        return await IssueItemizedAsync(
            invoiceType: InvoiceType.Pos,
            requestId: request.Id,
            customerId: request.CustomerId,
            visit: visit,
            batchId: null,
            number: request.Number,
            invoiceDiscount: request.DiscountAmount,
            requestItems: request.Items,
            payments: request.Payments,
            idempotencyKey: request.IdempotencyKey,
            deduction: deduction,
            // POS (clinic retail) bills at catalog price — contract pricing is a field-visit concern (PRD §6.6).
            applyContractPricing: false,
            pricingAsOf: DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime),
            cancellationToken: cancellationToken);
    }

    public async Task<InvoiceResponse> IssueFieldAsync(
        Guid visitId, FieldInvoiceRequest request, CancellationToken cancellationToken)
    {
        var visit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken)
                    ?? throw new NotFoundException("visit", visitId);

        // Field sales deduct from the visit doctor's field inventory (PRD §6.2).
        var deduction = await ResolveFieldLocationAsync(visit.DoctorId, cancellationToken);

        return await IssueItemizedAsync(
            invoiceType: InvoiceType.Field,
            requestId: request.Id,
            customerId: visit.CustomerId,
            visit: visit,
            batchId: request.BatchId ?? visit.BatchId,
            number: request.Number,
            invoiceDiscount: request.DiscountAmount,
            requestItems: request.Items,
            payments: request.Payments,
            idempotencyKey: request.IdempotencyKey,
            deduction: deduction,
            // Field visits to a contracted customer bill product lines at the contract-overridden price
            // when an active contract applies on the visit date (PRD §6.6); else catalog price.
            applyContractPricing: true,
            pricingAsOf: DateOnly.FromDateTime((visit.StartedAt ?? _clock.UtcNow).UtcDateTime),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Standalone exam-fee (Kashfiyya) invoice — no line items, no inventory (M7 task 6). The whole
    /// invoice is the fee; it posts an <c>exam_fee</c> ledger entry and is the System-B input M9
    /// credits to the doctor.
    /// </summary>
    public async Task<InvoiceResponse> IssueExamFeeAsync(
        Guid visitId, ExamFeeInvoiceRequest request, CancellationToken cancellationToken)
    {
        var (envId, userId) = RequireUser();

        var replayId = await _db.Invoices.AsNoTracking()
            .Where(i => i.EnvironmentId == envId && i.IdempotencyKey == request.IdempotencyKey)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (replayId is { } existing)
        {
            return await BuildResponseAsync(existing, cancellationToken);
        }

        var visit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken)
                    ?? throw new NotFoundException("visit", visitId);

        var settings = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.EnvironmentId == envId, cancellationToken);
        var fee = Money(request.Amount ?? visit.ExamFeeApplied ?? settings?.DefaultExamFee ?? 0m);

        var normalizedNumber = string.IsNullOrWhiteSpace(request.Number) ? null : request.Number.Trim();
        if (normalizedNumber is not null)
        {
            await _invoiceNumbers.ValidateAsync(normalizedNumber, excludeInvoiceId: null, cancellationToken);
        }

        if (request.Id is { } rid && rid != Guid.Empty
            && await _db.Invoices.IgnoreQueryFilters().AnyAsync(i => i.Id == rid, cancellationToken))
        {
            throw new ConflictException("invoice_id_collision", $"An invoice with id '{rid}' already exists.");
        }

        var paymentSum = Money(request.Payments.Sum(p => p.Amount));
        if (paymentSum > fee)
        {
            throw new ConflictException("payment_exceeds_total",
                $"Payments ({paymentSum}) exceed the exam fee ({fee}).");
        }

        var nonCreditPaid = Money(request.Payments.Where(p => p.Method != PaymentMethod.Credit).Sum(p => p.Amount));

        var invoice = new Invoice
        {
            Id = request.Id ?? Guid.Empty,
            InvoiceType = InvoiceType.ExamFee,
            CustomerId = visit.CustomerId,
            FarmId = await ResolveInvoiceFarmIdAsync(visit, visit.BatchId, cancellationToken),
            VisitId = visit.Id,
            BatchId = visit.BatchId,
            Number = normalizedNumber,
            Subtotal = fee,
            DiscountAmount = 0m,
            TaxAmount = 0m,
            Total = fee,
            Status = InvoiceStatus.Issued,
            IssuedBy = userId,
            IssuedAt = _clock.UtcNow,
            IdempotencyKey = request.IdempotencyKey,
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var payment in request.Payments)
        {
            _db.Payments.Add(new Payment
            {
                Id = payment.Id ?? Guid.Empty,
                InvoiceId = invoice.Id,
                Method = payment.Method,
                Amount = Money(payment.Amount),
                PaidAt = _clock.UtcNow,
                ChequeNumber = payment.ChequeNumber,
                ChequeBank = payment.ChequeBank,
                ChequeDueDate = payment.ChequeDueDate,
            });
        }

        await _db.SaveChangesAsync(cancellationToken);

        await PostInvoiceLedgerEntryAsync(invoice, fee - nonCreditPaid, cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return await BuildResponseAsync(invoice.Id, cancellationToken);
    }

    /// <summary>
    /// Voids an issued invoice (M7 task 10). Append-only: the original row is left untouched; a new
    /// <c>status='void'</c> invoice with negated totals points back to it via
    /// <c>void_of_invoice_id</c>, and a compensating <c>adjustment</c> ledger entry reverses the
    /// amount the original posted (skipped for walk-ins). Inventory is not auto-reversed — post a
    /// <c>return_add</c> movement to put stock back. Idempotent: re-voiding returns the existing void.
    /// </summary>
    public async Task<InvoiceResponse> VoidAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var (_, userId) = RequireUser();

        var original = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
                       ?? throw new NotFoundException("invoice", invoiceId);

        if (original.Status == InvoiceStatus.Void || original.VoidOfInvoiceId is not null)
        {
            throw new ConflictException("cannot_void_void_row", "A void row cannot itself be voided.");
        }

        // Idempotent replay: if this invoice was already voided, return the existing void row.
        var voidKey = $"void-{original.Id}";
        var existingVoid = await _db.Invoices.AsNoTracking()
            .Where(i => i.VoidOfInvoiceId == original.Id)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingVoid is { } already)
        {
            return await BuildResponseAsync(already, cancellationToken);
        }

        var voidInvoice = new Invoice
        {
            Id = Guid.Empty,
            InvoiceType = original.InvoiceType,
            CustomerId = original.CustomerId,
            FarmId = original.FarmId,
            VisitId = original.VisitId,
            BatchId = original.BatchId,
            Number = null, // server-side reversal marker; carries no client-prefixed number
            Subtotal = -original.Subtotal,
            DiscountAmount = -original.DiscountAmount,
            TaxAmount = -original.TaxAmount,
            Total = -original.Total,
            Status = InvoiceStatus.Void,
            IssuedBy = userId,
            IssuedAt = _clock.UtcNow,
            IdempotencyKey = voidKey,
            VoidOfInvoiceId = original.Id,
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.Invoices.Add(voidInvoice);
        await _db.SaveChangesAsync(cancellationToken);

        // Reverse exactly what the original posted, to the same owner ledger (farm or customer);
        // a walk-in original posted nothing, so there is nothing to reverse.
        var ownerLedgerId = await ResolveOwnerLedgerIdAsync(original.CustomerId, original.FarmId, cancellationToken);
        if (ownerLedgerId is { } reverseLedgerId)
        {
            var nonCreditPaid = await _db.Payments
                .Where(p => p.InvoiceId == original.Id && p.Method != PaymentMethod.Credit)
                .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;
            var reversal = -(original.Total - Money(nonCreditPaid));

            await _ledgers.AppendEntryAsync(
                new LedgerEntryRequest(
                    Id: null,
                    LedgerId: reverseLedgerId,
                    EntryType: LedgerEntryType.Adjustment,
                    Amount: reversal,
                    InvoiceId: voidInvoice.Id,
                    ReceiptVoucherId: null,
                    Description: original.Number is null ? "Void of invoice" : $"Void of invoice {original.Number}",
                    IdempotencyKey: $"void-ledger-{original.Id}"),
                cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        return await BuildResponseAsync(voidInvoice.Id, cancellationToken);
    }

    public async Task<InvoiceResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var exists = await _db.Invoices.AsNoTracking().AnyAsync(i => i.Id == id, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("invoice", id);
        }

        return await BuildResponseAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<InvoiceResponse>> ListAsync(
        Guid? customerId, Guid? visitId, string? status, int? skip, int? take, CancellationToken cancellationToken)
    {
        var query = _db.Invoices.AsNoTracking();
        if (customerId is { } cid) query = query.Where(i => i.CustomerId == cid);
        if (visitId is { } vid) query = query.Where(i => i.VisitId == vid);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(i => i.Status == status);

        var ids = await query
            .OrderByDescending(i => i.IssuedAt)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

        var results = new List<InvoiceResponse>(ids.Count);
        foreach (var id in ids)
        {
            results.Add(await BuildResponseAsync(id, cancellationToken));
        }

        return results;
    }

    /// <summary>
    /// The shared transactional issuance core (POS + field). Exam-fee issuance has no items and goes
    /// through <c>IssueExamFeeAsync</c> instead.
    /// </summary>
    internal async Task<InvoiceResponse> IssueItemizedAsync(
        string invoiceType,
        Guid? requestId,
        Guid? customerId,
        Visit? visit,
        Guid? batchId,
        string? number,
        decimal invoiceDiscount,
        IReadOnlyList<InvoiceLineRequest> requestItems,
        IReadOnlyList<PaymentRequest> payments,
        string idempotencyKey,
        DeductionLocation deduction,
        bool applyContractPricing,
        DateOnly pricingAsOf,
        CancellationToken cancellationToken)
    {
        var (envId, userId) = RequireUser();

        // Row-level idempotency replay (independent of the HTTP Idempotency-Key filter): the same
        // invoice key returns the originally issued invoice without re-deducting or re-posting.
        var replayId = await _db.Invoices.AsNoTracking()
            .Where(i => i.EnvironmentId == envId && i.IdempotencyKey == idempotencyKey)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (replayId is { } existing)
        {
            return await BuildResponseAsync(existing, cancellationToken);
        }

        if (customerId is { } cid)
        {
            await RequireExistsAsync(_db.Customers.AnyAsync(c => c.Id == cid, cancellationToken), "customer", cid);
        }

        var normalizedNumber = string.IsNullOrWhiteSpace(number) ? null : number.Trim();
        if (normalizedNumber is not null)
        {
            await _invoiceNumbers.ValidateAsync(normalizedNumber, excludeInvoiceId: null, cancellationToken);
        }

        if (requestId is { } rid && rid != Guid.Empty
            && await _db.Invoices.IgnoreQueryFilters().AnyAsync(i => i.Id == rid, cancellationToken))
        {
            throw new ConflictException("invoice_id_collision", $"An invoice with id '{rid}' already exists.");
        }

        var lines = new List<ResolvedLine>();
        foreach (var item in requestItems)
        {
            lines.Add(await ResolveExplicitLineAsync(item, customerId, visit, pricingAsOf, applyContractPricing, cancellationToken));
        }

        if (visit is not null)
        {
            // Charges the client already sent as explicit back-linked lines (price/discount edited
            // at the till) must not auto-assemble a second time.
            var explicitRx = requestItems
                .Where(i => i.PrescriptionId is not null).Select(i => i.PrescriptionId!.Value).ToHashSet();
            var explicitProcedures = requestItems
                .Where(i => i.ProcedureId is not null).Select(i => i.ProcedureId!.Value).ToHashSet();
            lines.AddRange(await AssembleVisitLinesAsync(
                visit, customerId, pricingAsOf, applyContractPricing, explicitRx, explicitProcedures, cancellationToken));
        }

        if (lines.Count == 0)
        {
            throw new ConflictException("invoice_has_no_lines",
                "Nothing to bill: no line items were supplied and the visit had no unbilled charges.");
        }

        var subtotal = Money(lines.Sum(l => l.LineTotal));
        var discount = Money(invoiceDiscount);
        var taxable = subtotal - discount;
        if (taxable < 0m)
        {
            throw new ConflictException("discount_exceeds_subtotal", "The invoice discount exceeds the subtotal.");
        }

        var settings = await _db.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.EnvironmentId == envId, cancellationToken);
        var taxAmount = settings is { TaxEnabled: true }
            ? Money(taxable * settings.TaxRate / 100m)
            : 0m;
        var total = taxable + taxAmount;

        var paymentSum = Money(payments.Sum(p => p.Amount));
        if (paymentSum > total)
        {
            throw new ConflictException("payment_exceeds_total",
                $"Payments ({paymentSum}) exceed the invoice total ({total}).");
        }

        var nonCreditPaid = Money(payments.Where(p => p.Method != PaymentMethod.Credit).Sum(p => p.Amount));

        var invoice = new Invoice
        {
            Id = requestId ?? Guid.Empty,
            InvoiceType = invoiceType,
            CustomerId = customerId,
            FarmId = await ResolveInvoiceFarmIdAsync(visit, batchId, cancellationToken),
            VisitId = visit?.Id,
            BatchId = batchId,
            Number = normalizedNumber,
            Subtotal = subtotal,
            DiscountAmount = discount,
            TaxAmount = taxAmount,
            Total = total,
            Status = InvoiceStatus.Issued,
            IssuedBy = userId,
            IssuedAt = _clock.UtcNow,
            IdempotencyKey = idempotencyKey,
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync(cancellationToken); // assigns invoice.Id

        foreach (var line in lines)
        {
            _db.InvoiceItems.Add(new InvoiceItem
            {
                Id = Guid.Empty,
                InvoiceId = invoice.Id,
                ProductId = line.ProductId,
                ServiceId = line.ServiceId,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                CostPrice = line.CostPrice,
                DiscountAmount = line.DiscountAmount,
                LineTotal = line.LineTotal,
                PrescriptionId = line.PrescriptionId,
                ProcedureId = line.ProcedureId,
            });
        }

        foreach (var payment in payments)
        {
            _db.Payments.Add(new Payment
            {
                Id = payment.Id ?? Guid.Empty,
                InvoiceId = invoice.Id,
                Method = payment.Method,
                Amount = Money(payment.Amount),
                PaidAt = _clock.UtcNow,
                ChequeNumber = payment.ChequeNumber,
                ChequeBank = payment.ChequeBank,
                ChequeDueDate = payment.ChequeDueDate,
            });
        }

        await _db.SaveChangesAsync(cancellationToken); // assigns item ids (used as movement keys below)

        // Deduct each product line from stock (services don't touch inventory). A negative-stock
        // rejection throws and rolls the whole issuance back (SCHEMA invariant #2).
        var productItems = await _db.InvoiceItems
            .Where(it => it.InvoiceId == invoice.Id && it.ProductId != null)
            .ToListAsync(cancellationToken);
        foreach (var item in productItems)
        {
            await _inventory.ApplyMovementAsync(
                new MovementIntent(
                    Id: null,
                    MovementType: MovementType.SaleDeduct,
                    ProductId: item.ProductId!.Value,
                    Quantity: item.Quantity,
                    FromLocationType: deduction.LocationType,
                    FromLocationId: deduction.LocationId,
                    ToLocationType: null,
                    ToLocationId: null,
                    IdempotencyKey: $"sale-{item.Id}",
                    Reason: $"invoice {invoice.Number ?? invoice.Id.ToString()}",
                    VisitId: invoice.VisitId,
                    InvoiceId: invoice.Id),
                cancellationToken);
        }

        // Ledger: an invoice posts its outstanding (credit) portion to its owner ledger — the farm
        // ledger for a farm-scoped invoice, else the customer ledger; a walk-in (no owner) posts nothing.
        await PostInvoiceLedgerEntryAsync(invoice, total - nonCreditPaid, cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return await BuildResponseAsync(invoice.Id, cancellationToken);
    }

    /// <summary>
    /// M16 — the ledger a charge posts to: the farm ledger when a <c>farm_id</c> is in play
    /// (<c>Invoice.FarmId</c> / <c>Visit.FarmId</c> / <c>Batch.FarmId</c>), else the customer ledger,
    /// else null (walk-in: no customer, skip the ledger). The owning ledger always exists — a farm
    /// gets one in its PUT path and a customer gets one on creation.
    /// </summary>
    internal async Task<Guid?> ResolveOwnerLedgerIdAsync(
        Guid? customerId, Guid? farmId, CancellationToken cancellationToken)
    {
        if (farmId is { } fid)
        {
            return await _db.Ledgers.Where(l => l.FarmId == fid).Select(l => (Guid?)l.Id)
                       .FirstOrDefaultAsync(cancellationToken)
                   ?? throw new NotFoundException("ledger", fid);
        }

        if (customerId is { } cid)
        {
            return await _db.Ledgers.Where(l => l.CustomerId == cid).Select(l => (Guid?)l.Id)
                       .FirstOrDefaultAsync(cancellationToken)
                   ?? throw new NotFoundException("ledger", cid);
        }

        return null;
    }

    private async Task PostInvoiceLedgerEntryAsync(Invoice invoice, decimal amount, CancellationToken cancellationToken)
    {
        var ledgerId = await ResolveOwnerLedgerIdAsync(invoice.CustomerId, invoice.FarmId, cancellationToken);
        if (ledgerId is not { } lid)
        {
            return; // walk-in: no owner ledger to post to
        }

        var entryType = invoice.InvoiceType == InvoiceType.ExamFee
            ? LedgerEntryType.ExamFee
            : LedgerEntryType.Invoice;

        await _ledgers.AppendEntryAsync(
            new LedgerEntryRequest(
                Id: null,
                LedgerId: lid,
                EntryType: entryType,
                Amount: amount,
                InvoiceId: invoice.Id,
                ReceiptVoucherId: null,
                Description: invoice.Number is null ? "Invoice" : $"Invoice {invoice.Number}",
                IdempotencyKey: $"invoice-{invoice.Id}"),
            cancellationToken);
    }

    private async Task<ResolvedLine> ResolveExplicitLineAsync(
        InvoiceLineRequest item,
        Guid? customerId,
        Visit? visit,
        DateOnly pricingAsOf,
        bool applyContractPricing,
        CancellationToken cancellationToken)
    {
        var quantity = item.Quantity;
        decimal? procedurePrice = null;

        // Back-linked visit charges (the POS presents them as locked-quantity cart lines so the
        // cashier can adjust price/discount): the clinical record stays authoritative for WHAT is
        // billed and HOW MANY — the request quantity is ignored. Auto-assembly skips these ids.
        if (item.PrescriptionId is { } rxId)
        {
            if (visit is null)
            {
                throw new ConflictException("line_backlink_requires_visit",
                    "A prescription-linked line requires the invoice to be linked to its visit.");
            }

            var rx = await _db.Prescriptions.AsNoTracking()
                         .FirstOrDefaultAsync(p => p.Id == rxId, cancellationToken)
                     ?? throw new NotFoundException("prescription", rxId);
            if (rx.VisitId != visit.Id)
            {
                throw new ConflictException("prescription_not_in_visit",
                    $"Prescription '{rxId}' does not belong to visit '{visit.Id}'.");
            }

            if (rx.DispenseType != DispenseType.DispensedToOwner)
            {
                throw new ConflictException("prescription_not_billable",
                    "Only dispensed_to_owner prescriptions are billable as invoice lines.");
            }

            if (rx.ProductId != item.ProductId)
            {
                throw new ConflictException("prescription_product_mismatch",
                    "The line's product_id must match the prescription's product.");
            }

            if (await _db.InvoiceItems.AsNoTracking().AnyAsync(it => it.PrescriptionId == rxId, cancellationToken))
            {
                throw new ConflictException("prescription_already_billed",
                    $"Prescription '{rxId}' is already billed on an invoice.");
            }

            quantity = rx.Quantity ?? 1m; // server-wins: quantity is edited on the visit, not at the till
        }
        else if (item.ProcedureId is { } procedureId)
        {
            if (visit is null)
            {
                throw new ConflictException("line_backlink_requires_visit",
                    "A procedure-linked line requires the invoice to be linked to its visit.");
            }

            var procedure = await _db.Procedures.AsNoTracking()
                                .FirstOrDefaultAsync(p => p.Id == procedureId, cancellationToken)
                            ?? throw new NotFoundException("procedure", procedureId);
            if (procedure.VisitId != visit.Id)
            {
                throw new ConflictException("procedure_not_in_visit",
                    $"Procedure '{procedureId}' does not belong to visit '{visit.Id}'.");
            }

            if (procedure.ServiceId is null || procedure.ServiceId != item.ServiceId)
            {
                throw new ConflictException("procedure_service_mismatch",
                    "The line's service_id must match the procedure's billable service.");
            }

            if (await _db.InvoiceItems.AsNoTracking().AnyAsync(it => it.ProcedureId == procedureId, cancellationToken))
            {
                throw new ConflictException("procedure_already_billed",
                    $"Procedure '{procedureId}' is already billed on an invoice.");
            }

            quantity = 1m;                    // a procedure always bills as a single line
            procedurePrice = procedure.Price; // the visit-set price, unless the request overrides it
        }

        decimal unitPrice;
        decimal costPrice;
        string? description = item.Description;

        if (item.ProductId is { } productId)
        {
            var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
                          ?? throw new NotFoundException("product", productId);
            // An explicit request unit_price always wins; otherwise field invoices resolve the
            // contract-overridden price (M8) and everything else falls back to the catalog price.
            unitPrice = item.UnitPrice
                ?? await ResolveProductUnitPriceAsync(productId, product.SellingPrice, customerId, pricingAsOf, applyContractPricing, cancellationToken);
            costPrice = product.PurchasePrice; // snapshot — never recomputed (SCHEMA invariant #8)
            description ??= product.NameAr;
        }
        else
        {
            var serviceId = item.ServiceId!.Value;
            var service = await _db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serviceId, cancellationToken)
                          ?? throw new NotFoundException("service", serviceId);
            unitPrice = item.UnitPrice ?? procedurePrice ?? service.DefaultPrice;
            costPrice = 0m;
            description ??= service.NameAr;
        }

        var lineTotal = Money(quantity * unitPrice - item.DiscountAmount);
        if (lineTotal < 0m)
        {
            throw new ConflictException("line_discount_exceeds_total",
                "A line discount cannot exceed the line's gross amount.");
        }

        return new ResolvedLine(
            item.ProductId, item.ServiceId, description, quantity, unitPrice, costPrice,
            Money(item.DiscountAmount), lineTotal, item.PrescriptionId, item.ProcedureId);
    }

    /// <summary>
    /// Auto-assembles a visit's unbilled charges (M7 task 8): every <c>dispensed_to_owner</c>
    /// prescription and every billable procedure that no existing invoice line already references —
    /// minus any the request already billed as explicit back-linked lines (the POS cart's editable
    /// visit lines). On field invoices, dispensed-med lines bill at the contract-overridden price
    /// when one applies (M8 task 10); procedures (services) are never contract-overridden and use
    /// the procedure price.
    /// </summary>
    private async Task<List<ResolvedLine>> AssembleVisitLinesAsync(
        Visit visit,
        Guid? customerId,
        DateOnly pricingAsOf,
        bool applyContractPricing,
        IReadOnlySet<Guid> excludePrescriptionIds,
        IReadOnlySet<Guid> excludeProcedureIds,
        CancellationToken cancellationToken)
    {
        var lines = new List<ResolvedLine>();

        var dispensed = await _db.Prescriptions.AsNoTracking()
            .Where(p => p.VisitId == visit.Id && p.DispenseType == DispenseType.DispensedToOwner)
            .ToListAsync(cancellationToken);

        if (dispensed.Count > 0)
        {
            var rxIds = dispensed.Select(p => p.Id).ToList();
            var billed = await _db.InvoiceItems.AsNoTracking()
                .Where(it => it.PrescriptionId != null && rxIds.Contains(it.PrescriptionId!.Value))
                .Select(it => it.PrescriptionId!.Value)
                .ToListAsync(cancellationToken);
            var billedSet = billed.ToHashSet();
            billedSet.UnionWith(excludePrescriptionIds);

            var productIds = dispensed.Select(p => p.ProductId).Distinct().ToList();
            var products = await _db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            foreach (var rx in dispensed.Where(p => !billedSet.Contains(p.Id)))
            {
                if (!products.TryGetValue(rx.ProductId, out var product))
                {
                    continue;
                }

                var quantity = rx.Quantity ?? 1m;
                var unitPrice = await ResolveProductUnitPriceAsync(
                    rx.ProductId, product.SellingPrice, customerId, pricingAsOf, applyContractPricing, cancellationToken);
                lines.Add(new ResolvedLine(
                    ProductId: rx.ProductId,
                    ServiceId: null,
                    Description: product.NameAr,
                    Quantity: quantity,
                    UnitPrice: unitPrice,
                    CostPrice: product.PurchasePrice,
                    DiscountAmount: 0m,
                    LineTotal: Money(quantity * unitPrice),
                    PrescriptionId: rx.Id,
                    ProcedureId: null));
            }
        }

        var procedures = await _db.Procedures.AsNoTracking()
            .Where(pr => pr.VisitId == visit.Id && pr.ServiceId != null)
            .ToListAsync(cancellationToken);

        if (procedures.Count > 0)
        {
            var procIds = procedures.Select(p => p.Id).ToList();
            var billedProc = await _db.InvoiceItems.AsNoTracking()
                .Where(it => it.ProcedureId != null && procIds.Contains(it.ProcedureId!.Value))
                .Select(it => it.ProcedureId!.Value)
                .ToListAsync(cancellationToken);
            var billedProcSet = billedProc.ToHashSet();
            billedProcSet.UnionWith(excludeProcedureIds);

            foreach (var procedure in procedures.Where(p => !billedProcSet.Contains(p.Id)))
            {
                lines.Add(new ResolvedLine(
                    ProductId: null,
                    ServiceId: procedure.ServiceId,
                    Description: null,
                    Quantity: 1m,
                    UnitPrice: procedure.Price,
                    CostPrice: 0m,
                    DiscountAmount: 0m,
                    LineTotal: Money(procedure.Price),
                    PrescriptionId: null,
                    ProcedureId: procedure.Id));
            }
        }

        return lines;
    }

    /// <summary>
    /// The default sale price for a product line (M8 task 10). On field invoices with a customer, the
    /// pricing service applies the active-contract override when one exists; otherwise — and always on
    /// POS — the catalog selling price is used. Cost is snapshotted separately and is unaffected.
    /// </summary>
    private async Task<decimal> ResolveProductUnitPriceAsync(
        Guid productId,
        decimal catalogSellingPrice,
        Guid? customerId,
        DateOnly pricingAsOf,
        bool applyContractPricing,
        CancellationToken cancellationToken)
    {
        if (!applyContractPricing || customerId is null)
        {
            return catalogSellingPrice;
        }

        var resolved = await _pricing.ResolveUnitPriceAsync(productId, customerId, pricingAsOf, cancellationToken);
        return resolved.IsContractPrice ? resolved.UnitPrice : catalogSellingPrice;
    }

    private async Task<InvoiceResponse> BuildResponseAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await _db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken)
                      ?? throw new NotFoundException("invoice", invoiceId);

        var items = await _db.InvoiceItems.AsNoTracking()
            .Where(it => it.InvoiceId == invoiceId)
            .OrderBy(it => it.CreatedAt)
            .ToListAsync(cancellationToken);

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.InvoiceId == invoiceId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return new InvoiceResponse(
            invoice.Id,
            invoice.InvoiceType,
            invoice.CustomerId,
            invoice.FarmId,
            invoice.VisitId,
            invoice.BatchId,
            invoice.Number,
            invoice.Subtotal,
            invoice.DiscountAmount,
            invoice.TaxAmount,
            invoice.Total,
            invoice.Status,
            invoice.IssuedBy,
            invoice.IssuedAt,
            invoice.VoidOfInvoiceId,
            items.Select(_mapper.Map<InvoiceItemResponse>).ToList(),
            payments.Select(_mapper.Map<PaymentResponse>).ToList(),
            invoice.CreatedAt,
            invoice.UpdatedAt);
    }

    /// <summary>
    /// M15 — the farm an invoice attributes to: the originating visit's farm, else the batch's farm,
    /// else null (POS / walk-in / in-clinic with no farm). No ledger-routing change this milestone.
    /// </summary>
    private async Task<Guid?> ResolveInvoiceFarmIdAsync(Visit? visit, Guid? batchId, CancellationToken cancellationToken)
    {
        if (visit?.FarmId is { } visitFarm)
        {
            return visitFarm;
        }

        if (batchId is { } bid)
        {
            return await _db.Batches
                .Where(b => b.Id == bid)
                .Select(b => b.FarmId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return null;
    }

    private async Task<Visit?> ResolveVisitAsync(Guid? visitId, CancellationToken cancellationToken)
    {
        if (visitId is not { } id)
        {
            return null;
        }

        return await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
               ?? throw new NotFoundException("visit", id);
    }

    private async Task<DeductionLocation> ResolveWarehouseLocationAsync(CancellationToken cancellationToken)
    {
        var warehouseId = await _db.Warehouses
            .Select(w => (Guid?)w.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ConflictException("no_warehouse",
                "No warehouse exists in this environment to deduct sold products from.");

        return new DeductionLocation(StockLocation.Warehouse, warehouseId);
    }

    private async Task<DeductionLocation> ResolveFieldLocationAsync(Guid doctorId, CancellationToken cancellationToken)
    {
        var fieldId = await _db.FieldInventories
            .Where(fi => fi.DoctorId == doctorId)
            .Select(fi => (Guid?)fi.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ConflictException("no_field_inventory",
                "The visit's field doctor has no field inventory to deduct sold products from.");

        return new DeductionLocation(StockLocation.Field, fieldId);
    }

    private (Guid EnvironmentId, Guid UserId) RequireUser()
    {
        if (_currentUser.EnvironmentId is not { } envId || _currentUser.UserId is not { } userId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        return (envId, userId);
    }

    private static async Task RequireExistsAsync(Task<bool> existsQuery, string entity, Guid id)
    {
        if (!await existsQuery)
        {
            throw new NotFoundException(entity, id);
        }
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    internal sealed record DeductionLocation(string LocationType, Guid LocationId);

    private sealed record ResolvedLine(
        Guid? ProductId,
        Guid? ServiceId,
        string? Description,
        decimal Quantity,
        decimal UnitPrice,
        decimal CostPrice,
        decimal DiscountAmount,
        decimal LineTotal,
        Guid? PrescriptionId,
        Guid? ProcedureId);
}
