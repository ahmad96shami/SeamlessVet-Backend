using FluentAssertions;
using VetSystem.API.Endpoints.Sync;
using VetSystem.Domain.Common;

namespace VetSystem.Tests.Financial;

/// <summary>
/// M7 task 16 (unit) — every financial /sync handler rejects PATCH and DELETE with a typed
/// append-only error (SCHEMA invariants #3 #5). The guards throw before touching any dependency, so
/// the handlers can be exercised without a DB.
/// </summary>
public sealed class FinancialSyncAppendOnlyTests
{
    public static IEnumerable<object[]> Handlers() =>
    [
        [new InvoicesSyncHandler(null!, null!, null!, null!), "invoices_append_only"],
        [new InvoiceItemsSyncHandler(null!, null!), "invoice_items_append_only"],
        [new PaymentsSyncHandler(null!, null!, null!), "payments_append_only"],
        [new ReceiptVouchersSyncHandler(null!, null!, null!), "receipt_vouchers_append_only"],
    ];

    [Theory]
    [MemberData(nameof(Handlers))]
    public async Task PatchAsync_ThrowsAppendOnly(ISyncTableHandler handler, string expectedCode)
    {
        var act = () => handler.PatchAsync(Guid.NewGuid(), default, CancellationToken.None);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(expectedCode);
    }

    [Theory]
    [MemberData(nameof(Handlers))]
    public async Task DeleteAsync_ThrowsAppendOnly(ISyncTableHandler handler, string expectedCode)
    {
        var act = () => handler.DeleteAsync(Guid.NewGuid(), CancellationToken.None);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(expectedCode);
    }
}
