namespace VetSystem.Application.Entitlements;

/// <summary>
/// System A — drug-profit entitlement (SCHEMA "Key invariants" #8, PRD §7.4):
/// <c>share = doctor_share_percent × (Σ(sale_value − cost) − exam_fee)</c>, capped at the optional
/// <c>doctor_share_ceiling</c>. Pure: the caller resolves each line's <c>sale_value</c> (the
/// contract-overridden price where one applies, else the line's <c>unit_price</c> — via
/// <see cref="VetSystem.Application.Contracts.IPricingService"/>, M9 task 6) and the exam fee
/// (via <see cref="IExamFeeCalculator"/>) before calling this. This keeps the full
/// {fee model} × {ceiling} × {toggle} matrix unit-testable without a database.
/// </summary>
public interface ISystemADrugProfitCalculator
{
    EntitlementAmount Calculate(SystemAInput input);
}

/// <summary>One product line's profit contribution: <c>(SaleValue − Cost) × Quantity</c>.
/// <see cref="Cost"/> is the <c>invoice_items.cost_price</c> snapshot.</summary>
public sealed record DrugProfitLine(decimal SaleValue, decimal Cost, decimal Quantity);

/// <summary>Inputs to the System A formula. <see cref="DoctorSharePercent"/> is 0–100;
/// <see cref="DoctorShareCeiling"/> is the optional payout cap (null = uncapped).</summary>
public sealed record SystemAInput(
    IReadOnlyList<DrugProfitLine> Lines,
    decimal ExamFee,
    decimal DoctorSharePercent,
    decimal? DoctorShareCeiling);

/// <summary>The computed doctor share. <see cref="CeilingApplied"/> is the cap value when it bound the
/// result (exit criteria: <c>ComputedAmount</c> then equals the cap), else null.</summary>
public sealed record EntitlementAmount(decimal ComputedAmount, decimal? CeilingApplied);

/// <inheritdoc cref="ISystemADrugProfitCalculator"/>
public sealed class SystemADrugProfitCalculator : ISystemADrugProfitCalculator
{
    public EntitlementAmount Calculate(SystemAInput input)
    {
        var totalProfit = input.Lines.Sum(l => (l.SaleValue - l.Cost) * l.Quantity);

        // Exam fee comes off the top before the percentage (PRD §7.4 step 3).
        var basis = totalProfit - input.ExamFee;
        var raw = input.DoctorSharePercent / 100m * basis;

        // A doctor's entitlement never goes negative — if the exam fee exceeds profit the clinic
        // absorbs the shortfall (PRD §7.4 step 6, "remainder goes to the clinic"). Clamp, then cap.
        var share = Round(Math.Max(raw, 0m));

        if (input.DoctorShareCeiling is { } ceiling && share > ceiling)
        {
            var cap = Round(ceiling);
            return new EntitlementAmount(cap, cap);
        }

        return new EntitlementAmount(share, CeilingApplied: null);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
