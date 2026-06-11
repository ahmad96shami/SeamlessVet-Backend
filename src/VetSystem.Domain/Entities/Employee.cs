using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// M31 (SCHEMA §4) — a person the clinic pays a salary to. The HR mirror of <see cref="Supplier"/> /
/// <see cref="DoctorPartner"/> on the accounts-payable side: each employee owns exactly one
/// <see cref="EmployeeLedger"/> (created with the employee) whose balance is the unpaid salary the clinic
/// owes them (a loan/advance drives it negative). The <see cref="UserId"/> link is <b>optional</b> — a
/// janitor or driver with no staff account is still an employee — and when present it is unique per
/// environment. <see cref="MonthlySalary"/> is what the <c>MonthlySalaryAccrualJob</c> accrues each month
/// while <see cref="Active"/>. Center-web only (admin/accountant); employees are not part of any
/// field-doctor sync scope, so there is no <c>/sync</c> path.
/// </summary>
public sealed class Employee : Entity
{
    /// <summary>Optional link to the staff account (null for a non-user employee, e.g. a janitor).</summary>
    public Guid? UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string? JobTitle { get; set; }

    public decimal MonthlySalary { get; set; }

    /// <summary>Whether the monthly accrual job pays this employee. Inactive employees stop accruing.</summary>
    public bool Active { get; set; } = true;

    public DateOnly? HiredAt { get; set; }

    public string? Notes { get; set; }
}
