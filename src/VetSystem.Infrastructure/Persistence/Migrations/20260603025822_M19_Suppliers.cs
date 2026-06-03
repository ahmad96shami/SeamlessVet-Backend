using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M19_Suppliers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_receipt_vouchers_method",
                table: "receipt_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_method",
                table: "payments");

            migrationBuilder.AddColumn<string>(
                name: "cheque_bank",
                table: "receipt_vouchers",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "cheque_due_date",
                table: "receipt_vouchers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cheque_number",
                table: "receipt_vouchers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cheque_bank",
                table: "payments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "cheque_due_date",
                table: "payments",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cheque_number",
                table: "payments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "purchase_invoice_id",
                table: "inventory_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    phone_primary = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    phone_secondary = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    tax_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "purchase_invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warehouse_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    subtotal = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    total = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    received_by = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_purchase_invoices", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_invoices_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoices_users_received_by",
                        column: x => x.received_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoices_warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_ledgers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_supplier_ledgers", x => x.id);
                    table.CheckConstraint("ck_supplier_ledgers_status", "status IN ('open','has_debt','closed')");
                    table.ForeignKey(
                        name: "fk_supplier_ledgers_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_supplier_payments", x => x.id);
                    table.CheckConstraint("ck_supplier_payments_method", "method IN ('cash','card','bank_transfer','cheque')");
                    table.ForeignKey(
                        name: "fk_supplier_payments_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_payments_users_paid_by",
                        column: x => x.paid_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_invoice_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_invoice_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_invoice_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchase_invoice_items_purchase_invoices_purchase_invoice_id",
                        column: x => x.purchase_invoice_id,
                        principalTable: "purchase_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    purchase_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_supplier_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_supplier_ledger_entries_type", "entry_type IN ('purchase_invoice','payment','adjustment')");
                    table.ForeignKey(
                        name: "fk_supplier_ledger_entries_purchase_invoices_purchase_invoice_",
                        column: x => x.purchase_invoice_id,
                        principalTable: "purchase_invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_ledger_entries_supplier_ledgers_supplier_ledger_id",
                        column: x => x.supplier_ledger_id,
                        principalTable: "supplier_ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_supplier_ledger_entries_supplier_payments_supplier_payment_",
                        column: x => x.supplier_payment_id,
                        principalTable: "supplier_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_receipt_vouchers_method",
                table: "receipt_vouchers",
                sql: "method IN ('cash','card','bank_transfer','credit','cheque')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_method",
                table: "payments",
                sql: "method IN ('cash','card','bank_transfer','credit','cheque')");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_purchase_invoice_id",
                table: "inventory_movements",
                column: "purchase_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_items_environment_id_deleted_at",
                table: "purchase_invoice_items",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_items_environment_id_updated_at",
                table: "purchase_invoice_items",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_items_invoice",
                table: "purchase_invoice_items",
                column: "purchase_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoice_items_product_id",
                table: "purchase_invoice_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_environment_id_deleted_at",
                table: "purchase_invoices",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_environment_id_updated_at",
                table: "purchase_invoices",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_received_by",
                table: "purchase_invoices",
                column: "received_by");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_supplier",
                table: "purchase_invoices",
                columns: new[] { "environment_id", "supplier_id", "received_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_supplier_id",
                table: "purchase_invoices",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchase_invoices_warehouse_id",
                table: "purchase_invoices",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "ux_purchase_invoices_env_idempotency",
                table: "purchase_invoices",
                columns: new[] { "environment_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supplier_ledger_entries_environment_id_deleted_at",
                table: "supplier_ledger_entries",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_ledger_entries_environment_id_updated_at",
                table: "supplier_ledger_entries",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_ledger_entries_ledger",
                table: "supplier_ledger_entries",
                columns: new[] { "supplier_ledger_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_ledger_entries_purchase_invoice_id",
                table: "supplier_ledger_entries",
                column: "purchase_invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_ledger_entries_supplier_payment_id",
                table: "supplier_ledger_entries",
                column: "supplier_payment_id");

            migrationBuilder.CreateIndex(
                name: "ux_supplier_ledger_entries_env_idempotency",
                table: "supplier_ledger_entries",
                columns: new[] { "environment_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supplier_ledgers_environment_id_deleted_at",
                table: "supplier_ledgers",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_ledgers_environment_id_updated_at",
                table: "supplier_ledgers",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_supplier_ledgers_supplier",
                table: "supplier_ledgers",
                column: "supplier_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_supplier_payments_environment_id_deleted_at",
                table: "supplier_payments",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_payments_environment_id_updated_at",
                table: "supplier_payments",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_payments_paid_by",
                table: "supplier_payments",
                column: "paid_by");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_payments_supplier",
                table: "supplier_payments",
                columns: new[] { "environment_id", "supplier_id", "paid_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_payments_supplier_id",
                table: "supplier_payments",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ux_supplier_payments_env_idempotency",
                table: "supplier_payments",
                columns: new[] { "environment_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_environment_id_deleted_at",
                table: "suppliers",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_environment_id_updated_at",
                table: "suppliers",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_name",
                table: "suppliers",
                columns: new[] { "environment_id", "name" });

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_movements_purchase_invoices_purchase_invoice_id",
                table: "inventory_movements",
                column: "purchase_invoice_id",
                principalTable: "purchase_invoices",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_inventory_movements_purchase_invoices_purchase_invoice_id",
                table: "inventory_movements");

            migrationBuilder.DropTable(
                name: "purchase_invoice_items");

            migrationBuilder.DropTable(
                name: "supplier_ledger_entries");

            migrationBuilder.DropTable(
                name: "purchase_invoices");

            migrationBuilder.DropTable(
                name: "supplier_ledgers");

            migrationBuilder.DropTable(
                name: "supplier_payments");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_receipt_vouchers_method",
                table: "receipt_vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_method",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_inventory_movements_purchase_invoice_id",
                table: "inventory_movements");

            migrationBuilder.DropColumn(
                name: "cheque_bank",
                table: "receipt_vouchers");

            migrationBuilder.DropColumn(
                name: "cheque_due_date",
                table: "receipt_vouchers");

            migrationBuilder.DropColumn(
                name: "cheque_number",
                table: "receipt_vouchers");

            migrationBuilder.DropColumn(
                name: "cheque_bank",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "cheque_due_date",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "cheque_number",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "purchase_invoice_id",
                table: "inventory_movements");

            migrationBuilder.AddCheckConstraint(
                name: "ck_receipt_vouchers_method",
                table: "receipt_vouchers",
                sql: "method IN ('cash','card','bank_transfer','credit')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_method",
                table: "payments",
                sql: "method IN ('cash','card','bank_transfer','credit')");
        }
    }
}
