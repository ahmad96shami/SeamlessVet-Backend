using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M25_InventoryLots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "expiration_date",
                table: "purchase_invoice_items",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "lot_id",
                table: "inventory_movements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "inventory_lots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_invoice_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unit_cost = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    expiration_date = table.Column<DateOnly>(type: "date", nullable: true),
                    lot_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    received_qty = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    remaining_qty = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_lots", x => x.id);
                    table.CheckConstraint("ck_inventory_lots_location_type", "location_type IN ('warehouse','field')");
                    table.ForeignKey(
                        name: "fk_inventory_lots_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_lots_purchase_invoice_items_purchase_invoice_item",
                        column: x => x.purchase_invoice_item_id,
                        principalTable: "purchase_invoice_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_lot_id",
                table: "inventory_movements",
                column: "lot_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_lots_environment_id_deleted_at",
                table: "inventory_lots",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_lots_environment_id_updated_at",
                table: "inventory_lots",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_lots_fefo",
                table: "inventory_lots",
                columns: new[] { "location_type", "location_id", "product_id", "expiration_date" },
                filter: "remaining_qty > 0 AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_lots_product",
                table: "inventory_lots",
                columns: new[] { "environment_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_lots_product_id",
                table: "inventory_lots",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_lots_purchase_invoice_item_id",
                table: "inventory_lots",
                column: "purchase_invoice_item_id");

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_movements_inventory_lots_lot_id",
                table: "inventory_movements",
                column: "lot_id",
                principalTable: "inventory_lots",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_inventory_movements_inventory_lots_lot_id",
                table: "inventory_movements");

            migrationBuilder.DropTable(
                name: "inventory_lots");

            migrationBuilder.DropIndex(
                name: "ix_inventory_movements_lot_id",
                table: "inventory_movements");

            migrationBuilder.DropColumn(
                name: "expiration_date",
                table: "purchase_invoice_items");

            migrationBuilder.DropColumn(
                name: "lot_id",
                table: "inventory_movements");
        }
    }
}
