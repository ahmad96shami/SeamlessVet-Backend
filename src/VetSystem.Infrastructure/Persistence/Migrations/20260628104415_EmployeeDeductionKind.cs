using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmployeeDeductionKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_employee_payments_kind",
                table: "employee_payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_employee_ledger_entries_type",
                table: "employee_ledger_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_employee_payments_kind",
                table: "employee_payments",
                sql: "kind IN ('salary_payment','loan','loan_repayment','deduction')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_employee_ledger_entries_type",
                table: "employee_ledger_entries",
                sql: "entry_type IN ('salary_accrual','salary_payment','loan','loan_repayment','adjustment','deduction')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_employee_payments_kind",
                table: "employee_payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_employee_ledger_entries_type",
                table: "employee_ledger_entries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_employee_payments_kind",
                table: "employee_payments",
                sql: "kind IN ('salary_payment','loan','loan_repayment')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_employee_ledger_entries_type",
                table: "employee_ledger_entries",
                sql: "entry_type IN ('salary_accrual','salary_payment','loan','loan_repayment','adjustment')");
        }
    }
}
