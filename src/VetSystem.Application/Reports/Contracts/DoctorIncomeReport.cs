namespace VetSystem.Application.Reports.Contracts;

/// <summary>
/// One doctor's line in the doctor-income report (M12 task 2, PRD §7.9). <see cref="CalculatedShare"/>
/// is the doctor's recognised income (Σ of their computed entitlements); <see cref="TotalRevenue"/> is
/// the net-of-void revenue of the invoices tied to their visits, for context against the share.
/// </summary>
public sealed record DoctorIncomeRow(
    Guid DoctorId,
    string DoctorName,
    int VisitCount,
    decimal TotalRevenue,
    decimal CalculatedShare);

/// <summary>
/// Doctor-income report (PRD §7.9 "Doctor income (detailed)"). <see cref="Rows"/> is the requested
/// page; the <c>Total*</c> fields summarise the whole filtered set (all doctors), so KPIs stay correct
/// regardless of paging. <see cref="From"/>/<see cref="To"/>/<see cref="VisitType"/> echo the request.
/// </summary>
public sealed record DoctorIncomeReportResponse(
    DateOnly? From,
    DateOnly? To,
    string? VisitType,
    int DoctorCount,
    int TotalVisitCount,
    decimal TotalRevenue,
    decimal TotalCalculatedShare,
    IReadOnlyList<DoctorIncomeRow> Rows);
