using FluentAssertions;
using VetSystem.Application.Entitlements;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;

namespace VetSystem.Tests.Entitlements;

/// <summary>
/// M9 task 16 — the four exam-fee models (PRD §7.3) with their edge cases (zero birds, 100% percent,
/// zero invoice), plus the factory dispatch on <c>supervision_fee_model</c>. Pure, no database.
/// </summary>
public sealed class ExamFeeCalculatorTests
{
    private readonly IExamFeeCalculatorFactory _factory = new ExamFeeCalculatorFactory(
    [
        new FixedAmountExamFeeCalculator(),
        new PercentOfInvoiceExamFeeCalculator(),
        new PerBirdExamFeeCalculator(),
        new PerBatchFixedExamFeeCalculator(),
    ]);

    [Theory]
    [InlineData(150, 1000, 5000, 150)]   // flat fee ignores birds + invoice
    [InlineData(0, 1000, 5000, 0)]       // zero fee
    public void FixedAmount_ReturnsTheFlatValue(decimal feeValue, int animals, decimal invoice, decimal expected)
    {
        _factory.For(FeeModel.FixedAmount)
            .Calculate(new ExamFeeBasis(feeValue, animals, invoice))
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(10, 5000, 500)]    // 10% of 5000
    [InlineData(100, 5000, 5000)]  // 100% → the whole invoice
    [InlineData(10, 0, 0)]         // zero invoice → 0 regardless of rate
    public void PercentOfInvoice_AppliesRateToInvoiceTotal(decimal percent, decimal invoice, decimal expected)
    {
        _factory.For(FeeModel.PercentOfInvoice)
            .Calculate(new ExamFeeBasis(percent, AnimalCount: 0, invoice))
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(2, 1000, 2000)]   // 2 per bird × 1000 birds
    [InlineData(2, 0, 0)]         // zero birds → 0
    public void PerBird_MultipliesRateByAnimalCount(decimal rate, int animals, decimal expected)
    {
        _factory.For(FeeModel.PerBird)
            .Calculate(new ExamFeeBasis(rate, animals, InvoiceTotal: 0))
            .Should().Be(expected);
    }

    [Fact]
    public void FractionalFees_RoundToTheCent()
    {
        // 7.5% of 1234.56 = 92.592 → 92.59
        _factory.For(FeeModel.PercentOfInvoice)
            .Calculate(new ExamFeeBasis(7.5m, AnimalCount: 0, InvoiceTotal: 1234.56m))
            .Should().Be(92.59m);

        // 0.5 per bird × 333 birds = 166.50
        _factory.For(FeeModel.PerBird)
            .Calculate(new ExamFeeBasis(0.5m, AnimalCount: 333, InvoiceTotal: 0))
            .Should().Be(166.50m);
    }

    [Theory]
    [InlineData(3000, 3000)]
    [InlineData(0, 0)]
    public void PerBatchFixed_ReturnsTheCycleFlatValue(decimal feeValue, decimal expected)
    {
        _factory.For(FeeModel.PerBatchFixed)
            .Calculate(new ExamFeeBasis(feeValue, AnimalCount: 5000, InvoiceTotal: 99999))
            .Should().Be(expected);
    }

    [Fact]
    public void Factory_UnknownModel_IsRejected()
    {
        var act = () => _factory.For("monthly_retainer");
        act.Should().Throw<ConflictException>().Which.Code.Should().Be("invalid_fee_model");
    }

    [Theory]
    [InlineData(FeeModel.FixedAmount)]
    [InlineData(FeeModel.PercentOfInvoice)]
    [InlineData(FeeModel.PerBird)]
    [InlineData(FeeModel.PerBatchFixed)]
    public void Factory_ResolvesEveryCatalogModel(string model)
    {
        _factory.For(model).FeeModel.Should().Be(model);
    }
}
