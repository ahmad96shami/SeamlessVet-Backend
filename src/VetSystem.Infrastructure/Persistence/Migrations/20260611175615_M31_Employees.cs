using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M31_Employees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    job_title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    monthly_salary = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    hired_at = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                    table.ForeignKey(
                        name: "fk_employees_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_ledgers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_ledgers", x => x.id);
                    table.CheckConstraint("ck_employee_ledgers_status", "status IN ('open','has_debt','closed')");
                    table.ForeignKey(
                        name: "fk_employee_ledgers_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    loan_repayment_amount = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    paid_by = table.Column<Guid>(type: "uuid", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    cheque_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    cheque_bank = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    cheque_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_payments", x => x.id);
                    table.CheckConstraint("ck_employee_payments_kind", "kind IN ('salary_payment','loan','loan_repayment')");
                    table.CheckConstraint("ck_employee_payments_method", "method IN ('cash','card','bank_transfer','cheque')");
                    table.ForeignKey(
                        name: "fk_employee_payments_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_payments_users_paid_by",
                        column: x => x.paid_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    employee_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_employee_ledger_entries_type", "entry_type IN ('salary_accrual','salary_payment','loan','loan_repayment','adjustment')");
                    table.ForeignKey(
                        name: "fk_employee_ledger_entries_employee_ledgers_employee_ledger_id",
                        column: x => x.employee_ledger_id,
                        principalTable: "employee_ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_ledger_entries_employee_payments_employee_payment_",
                        column: x => x.employee_payment_id,
                        principalTable: "employee_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_employee_ledger_entries_employee_payment_id",
                table: "employee_ledger_entries",
                column: "employee_payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_ledger_entries_environment_id_deleted_at",
                table: "employee_ledger_entries",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_ledger_entries_environment_id_updated_at",
                table: "employee_ledger_entries",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_ledger_entries_ledger",
                table: "employee_ledger_entries",
                columns: new[] { "employee_ledger_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_employee_ledger_entries_env_idempotency",
                table: "employee_ledger_entries",
                columns: new[] { "environment_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_ledgers_environment_id_deleted_at",
                table: "employee_ledgers",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_ledgers_environment_id_updated_at",
                table: "employee_ledgers",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_employee_ledgers_employee",
                table: "employee_ledgers",
                column: "employee_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_payments_employee",
                table: "employee_payments",
                columns: new[] { "environment_id", "employee_id", "paid_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_employee_payments_employee_id",
                table: "employee_payments",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_payments_environment_id_deleted_at",
                table: "employee_payments",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_payments_environment_id_updated_at",
                table: "employee_payments",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_payments_paid_by",
                table: "employee_payments",
                column: "paid_by");

            migrationBuilder.CreateIndex(
                name: "ux_employee_payments_env_idempotency",
                table: "employee_payments",
                columns: new[] { "environment_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_environment_id_deleted_at",
                table: "employees",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_environment_id_updated_at",
                table: "employees",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_user_id",
                table: "employees",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_employees_user",
                table: "employees",
                columns: new[] { "environment_id", "user_id" },
                unique: true,
                filter: "user_id IS NOT NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employee_ledger_entries");

            migrationBuilder.DropTable(
                name: "employee_ledgers");

            migrationBuilder.DropTable(
                name: "employee_payments");

            migrationBuilder.DropTable(
                name: "employees");
        }
    }
}
