using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M7_Financial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    total = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    issued_by = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    void_of_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.CheckConstraint("ck_invoices_status", "status IN ('issued','flagged','void')");
                    table.CheckConstraint("ck_invoices_type", "invoice_type IN ('pos','field','exam_fee')");
                    table.ForeignKey(
                        name: "fk_invoices_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_invoices_void_of_invoice_id",
                        column: x => x.void_of_invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_users_issued_by",
                        column: x => x.issued_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receipt_vouchers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    issued_by = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipt_vouchers", x => x.id);
                    table.CheckConstraint("ck_receipt_vouchers_method", "method IN ('cash','card','bank_transfer','credit')");
                    table.ForeignKey(
                        name: "fk_receipt_vouchers_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_receipt_vouchers_users_issued_by",
                        column: x => x.issued_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    cost_price = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    prescription_id = table.Column<Guid>(type: "uuid", nullable: true),
                    procedure_id = table.Column<Guid>(type: "uuid", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_items", x => x.id);
                    table.CheckConstraint("chk_item_target", "(product_id IS NOT NULL AND service_id IS NULL) OR (product_id IS NULL AND service_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_invoice_items_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_items_prescriptions_prescription_id",
                        column: x => x.prescription_id,
                        principalTable: "prescriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_items_procedures_procedure_id",
                        column: x => x.procedure_id,
                        principalTable: "procedures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_items_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_method", "method IN ('cash','card','bank_transfer','credit')");
                    table.ForeignKey(
                        name: "fk_payments_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_invoice_id",
                table: "ledger_entries",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_receipt_voucher_id",
                table: "ledger_entries",
                column: "receipt_voucher_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_invoice_id",
                table: "inventory_movements",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_environment_id_deleted_at",
                table: "invoice_items",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_environment_id_updated_at",
                table: "invoice_items",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_invoice",
                table: "invoice_items",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_prescription_id",
                table: "invoice_items",
                column: "prescription_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_procedure_id",
                table: "invoice_items",
                column: "procedure_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_product_id",
                table: "invoice_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_service_id",
                table: "invoice_items",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_batch",
                table: "invoices",
                columns: new[] { "environment_id", "batch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_customer",
                table: "invoices",
                columns: new[] { "environment_id", "customer_id", "issued_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_customer_id",
                table: "invoices",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_environment_id_deleted_at",
                table: "invoices",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_environment_id_updated_at",
                table: "invoices",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_issued_by",
                table: "invoices",
                column: "issued_by");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_visit_id",
                table: "invoices",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_void_of_invoice_id",
                table: "invoices",
                column: "void_of_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ux_invoices_env_idempotency",
                table: "invoices",
                columns: new[] { "environment_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_invoices_env_number",
                table: "invoices",
                columns: new[] { "environment_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_environment_id_deleted_at",
                table: "payments",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_environment_id_updated_at",
                table: "payments",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_invoice",
                table: "payments",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_vouchers_customer_id",
                table: "receipt_vouchers",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipt_vouchers_environment_id_deleted_at",
                table: "receipt_vouchers",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_receipt_vouchers_environment_id_updated_at",
                table: "receipt_vouchers",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_receipt_vouchers_issued_by",
                table: "receipt_vouchers",
                column: "issued_by");

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_customer",
                table: "receipt_vouchers",
                columns: new[] { "environment_id", "customer_id", "issued_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ux_receipt_vouchers_env_idempotency",
                table: "receipt_vouchers",
                columns: new[] { "environment_id", "idempotency_key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_movements_invoices_invoice_id",
                table: "inventory_movements",
                column: "invoice_id",
                principalTable: "invoices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ledger_entries_invoices_invoice_id",
                table: "ledger_entries",
                column: "invoice_id",
                principalTable: "invoices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_ledger_entries_receipt_vouchers_receipt_voucher_id",
                table: "ledger_entries",
                column: "receipt_voucher_id",
                principalTable: "receipt_vouchers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_inventory_movements_invoices_invoice_id",
                table: "inventory_movements");

            migrationBuilder.DropForeignKey(
                name: "fk_ledger_entries_invoices_invoice_id",
                table: "ledger_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_ledger_entries_receipt_vouchers_receipt_voucher_id",
                table: "ledger_entries");

            migrationBuilder.DropTable(
                name: "invoice_items");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "receipt_vouchers");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropIndex(
                name: "ix_ledger_entries_invoice_id",
                table: "ledger_entries");

            migrationBuilder.DropIndex(
                name: "ix_ledger_entries_receipt_voucher_id",
                table: "ledger_entries");

            migrationBuilder.DropIndex(
                name: "ix_inventory_movements_invoice_id",
                table: "inventory_movements");
        }
    }
}
