using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M30_DoctorPartners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "doctor_partners",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_partners", x => x.id);
                    table.ForeignKey(
                        name: "fk_doctor_partners_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "doctor_partner_ledgers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_partner_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_doctor_partner_ledgers", x => x.id);
                    table.CheckConstraint("ck_doctor_partner_ledgers_status", "status IN ('open','has_debt','closed')");
                    table.ForeignKey(
                        name: "fk_doctor_partner_ledgers_doctor_partners_doctor_partner_id",
                        column: x => x.doctor_partner_id,
                        principalTable: "doctor_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "doctor_partner_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
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
                    table.PrimaryKey("pk_doctor_partner_payments", x => x.id);
                    table.CheckConstraint("ck_doctor_partner_payments_method", "method IN ('cash','card','bank_transfer','cheque')");
                    table.ForeignKey(
                        name: "fk_doctor_partner_payments_doctor_partners_doctor_partner_id",
                        column: x => x.doctor_partner_id,
                        principalTable: "doctor_partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_doctor_partner_payments_users_paid_by",
                        column: x => x.paid_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "doctor_partner_ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_partner_ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    doctor_entitlement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    doctor_partner_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_doctor_partner_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_doctor_partner_ledger_entries_type", "entry_type IN ('entitlement','payment','adjustment')");
                    table.ForeignKey(
                        name: "fk_doctor_partner_ledger_entries_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_doctor_partner_ledger_entries_doctor_entitlements_doctor_en",
                        column: x => x.doctor_entitlement_id,
                        principalTable: "doctor_entitlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_doctor_partner_ledger_entries_doctor_partner_ledgers_doctor",
                        column: x => x.doctor_partner_ledger_id,
                        principalTable: "doctor_partner_ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_doctor_partner_ledger_entries_doctor_partner_payments_docto",
                        column: x => x.doctor_partner_payment_id,
                        principalTable: "doctor_partner_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_partner_ledger_entries_batch_id",
                table: "doctor_partner_ledger_entries",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_partner_ledger_entries_doctor_entitlement_id",
                table: "doctor_partner_ledger_entries",
                column: "doctor_entitlement_id");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_partner_ledger_entries_doctor_partner_payment_id",
                table: "doctor_partner_ledger_entries",
                column: "doctor_partner_payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_partner_ledger_entries_environment_id_deleted_at",
                table: "doctor_partner_ledger_entries",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_partner_ledger_entries_environment_id_updated_at",
                table: "doctor_partner_ledger_entries",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_partner_ledger_entries_ledger",
                table: "doctor_partner_ledger_entries",
                columns: new[] { "doctor_partner_ledger_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_doctor_partner_ledger_entries_env_idempotency",
                table: "doctor_partner_ledger_entries",
                columns: new[] { "environment_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_doctor_partner_ledgers_environment_id_deleted_at",
                table: "doctor_partner_ledgers",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_partner_ledgers_environment_id_updated_at",
                table: "doctor_partner_ledgers",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_doctor_partner_ledgers_partner",
                table: "doctor_partner_ledgers",
                column: "doctor_partner_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_doctor_partner_payments_doctor_partner_id",
                table: "doctor_partner_payments",
                column: "doctor_partner_id");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_partner_payments_environment_id_deleted_at",
                table: "doctor_partner_payments",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_partner_payments_environment_id_updated_at",
                table: "doctor_partner_payments",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_partner_payments_paid_by",
                table: "doctor_partner_payments",
                column: "paid_by");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_partner_payments_partner",
                table: "doctor_partner_payments",
                columns: new[] { "environment_id", "doctor_partner_id", "paid_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_doctor_partner_payments_env_idempotency",
                table: "doctor_partner_payments",
                columns: new[] { "environment_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_doctor_partners_environment_id_deleted_at",
                table: "doctor_partners",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_partners_environment_id_updated_at",
                table: "doctor_partners",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_partners_user_id",
                table: "doctor_partners",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_doctor_partners_user",
                table: "doctor_partners",
                columns: new[] { "environment_id", "user_id" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "doctor_partner_ledger_entries");

            migrationBuilder.DropTable(
                name: "doctor_partner_ledgers");

            migrationBuilder.DropTable(
                name: "doctor_partner_payments");

            migrationBuilder.DropTable(
                name: "doctor_partners");
        }
    }
}
