using FluentAssertions;
using VetSystem.Application.Inventory;
using VetSystem.Application.Inventory.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;

namespace VetSystem.Tests.Inventory;

/// <summary>
/// M4 task 16 — pure unit tests (no database) of <see cref="MovementTranslator"/>: each of the six
/// movement types produces the correct signed delta(s) at each leg. The affected location is
/// asserted via <see cref="PlannedMovementLeg.AffectedLocationType"/>/<c>AffectedLocationId</c>
/// (to when delta &gt; 0, from when &lt; 0). Two-leg transfers must net to zero.
/// </summary>
public sealed class MovementTranslatorTests
{
    private static readonly Guid Warehouse = Guid.Parse("01900000-0000-7000-8000-0000000000a1");
    private static readonly Guid Field = Guid.Parse("01900000-0000-7000-8000-0000000000b2");
    private static readonly Guid Product = Guid.Parse("01900000-0000-7000-8000-0000000000c3");

    private readonly MovementTranslator _translator = new();

    [Fact]
    public void Receive_AddsToWarehouse()
    {
        var legs = _translator.Translate(Intent(MovementType.Receive, 5m,
            toType: StockLocation.Warehouse, toId: Warehouse));

        legs.Should().ContainSingle();
        legs[0].SignedDelta.Should().Be(5m);
        legs[0].AffectedLocationType.Should().Be(StockLocation.Warehouse);
        legs[0].AffectedLocationId.Should().Be(Warehouse);
        legs[0].FromLocationType.Should().BeNull();
    }

    [Fact]
    public void ReturnAdd_AddsToLocation()
    {
        var legs = _translator.Translate(Intent(MovementType.ReturnAdd, 3m,
            toType: StockLocation.Field, toId: Field));

        legs.Should().ContainSingle();
        legs[0].SignedDelta.Should().Be(3m);
        legs[0].AffectedLocationId.Should().Be(Field);
    }

    [Fact]
    public void SaleDeduct_SubtractsFromLocation()
    {
        var legs = _translator.Translate(Intent(MovementType.SaleDeduct, 2m,
            fromType: StockLocation.Field, fromId: Field));

        legs.Should().ContainSingle();
        legs[0].SignedDelta.Should().Be(-2m);
        legs[0].AffectedLocationType.Should().Be(StockLocation.Field);
        legs[0].AffectedLocationId.Should().Be(Field);
        legs[0].ToLocationType.Should().BeNull();
    }

    [Fact]
    public void Adjust_Positive_AddsToLocation()
    {
        var legs = _translator.Translate(Intent(MovementType.Adjust, 4m,
            toType: StockLocation.Warehouse, toId: Warehouse));

        legs.Should().ContainSingle();
        legs[0].SignedDelta.Should().Be(4m);
        legs[0].AffectedLocationId.Should().Be(Warehouse);
    }

    [Fact]
    public void Adjust_Negative_SubtractsFromLocation()
    {
        var legs = _translator.Translate(Intent(MovementType.Adjust, -4m,
            toType: StockLocation.Warehouse, toId: Warehouse));

        legs.Should().ContainSingle();
        legs[0].SignedDelta.Should().Be(-4m);
        legs[0].AffectedLocationType.Should().Be(StockLocation.Warehouse);
        legs[0].AffectedLocationId.Should().Be(Warehouse);
        legs[0].FromLocationId.Should().Be(Warehouse);
    }

    [Fact]
    public void LoadToField_DebitsWarehouse_CreditsField()
    {
        var legs = _translator.Translate(Intent(MovementType.LoadToField, 7m,
            fromType: StockLocation.Warehouse, fromId: Warehouse,
            toType: StockLocation.Field, toId: Field));

        legs.Should().HaveCount(2);

        var warehouseLeg = legs.Single(l => l.AffectedLocationId == Warehouse);
        var fieldLeg = legs.Single(l => l.AffectedLocationId == Field);

        warehouseLeg.SignedDelta.Should().Be(-7m, "stock leaves the warehouse");
        fieldLeg.SignedDelta.Should().Be(7m, "stock arrives at the field inventory");
        legs.Sum(l => l.SignedDelta).Should().Be(0m, "a transfer conserves total quantity");
    }

    [Fact]
    public void UnloadFromField_DebitsField_CreditsWarehouse()
    {
        var legs = _translator.Translate(Intent(MovementType.UnloadFromField, 7m,
            fromType: StockLocation.Field, fromId: Field,
            toType: StockLocation.Warehouse, toId: Warehouse));

        legs.Should().HaveCount(2);
        legs.Single(l => l.AffectedLocationId == Field).SignedDelta.Should().Be(-7m);
        legs.Single(l => l.AffectedLocationId == Warehouse).SignedDelta.Should().Be(7m);
        legs.Sum(l => l.SignedDelta).Should().Be(0m);
    }

    [Theory]
    [InlineData(MovementType.Receive)]
    [InlineData(MovementType.SaleDeduct)]
    [InlineData(MovementType.LoadToField)]
    public void NonPositiveQuantity_IsRejected(string movementType)
    {
        var intent = Intent(movementType, 0m,
            fromType: StockLocation.Warehouse, fromId: Warehouse,
            toType: movementType == MovementType.LoadToField ? StockLocation.Field : StockLocation.Warehouse,
            toId: Field);

        var act = () => _translator.Translate(intent);
        act.Should().Throw<ConflictException>().Which.Code.Should().Be("invalid_quantity");
    }

    [Fact]
    public void Receive_WithoutWarehouseTarget_IsRejected()
    {
        var act = () => _translator.Translate(Intent(MovementType.Receive, 5m,
            toType: StockLocation.Field, toId: Field));

        act.Should().Throw<ConflictException>().Which.Code.Should().Be("invalid_location");
    }

    [Fact]
    public void LoadToField_FromFieldInsteadOfWarehouse_IsRejected()
    {
        var act = () => _translator.Translate(Intent(MovementType.LoadToField, 5m,
            fromType: StockLocation.Field, fromId: Field,
            toType: StockLocation.Field, toId: Field));

        act.Should().Throw<ConflictException>().Which.Code.Should().Be("invalid_location");
    }

    [Fact]
    public void UnknownMovementType_IsRejected()
    {
        var act = () => _translator.Translate(Intent("teleport", 5m,
            toType: StockLocation.Warehouse, toId: Warehouse));

        act.Should().Throw<ConflictException>().Which.Code.Should().Be("invalid_movement_type");
    }

    private static MovementIntent Intent(
        string movementType,
        decimal quantity,
        string? fromType = null,
        Guid? fromId = null,
        string? toType = null,
        Guid? toId = null) =>
        new(
            Id: Guid.CreateVersion7(),
            MovementType: movementType,
            ProductId: Product,
            Quantity: quantity,
            FromLocationType: fromType,
            FromLocationId: fromId,
            ToLocationType: toType,
            ToLocationId: toId,
            IdempotencyKey: "test-key");
}
