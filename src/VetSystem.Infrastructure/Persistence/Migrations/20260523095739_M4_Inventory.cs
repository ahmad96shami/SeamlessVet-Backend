using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M4_Inventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "field_inventories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_field_inventories", x => x.id);
                    table.ForeignKey(
                        name: "fk_field_inventories_users_doctor_id",
                        column: x => x.doctor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    movement_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    from_location_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    from_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_location_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    to_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity_delta = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    performed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_movements", x => x.id);
                    table.CheckConstraint("ck_inventory_movements_from_location_type", "from_location_type IS NULL OR from_location_type IN ('warehouse','field')");
                    table.CheckConstraint("ck_inventory_movements_to_location_type", "to_location_type IS NULL OR to_location_type IN ('warehouse','field')");
                    table.CheckConstraint("ck_inventory_movements_type", "movement_type IN ('receive','adjust','load_to_field','unload_from_field','sale_deduct','return_add')");
                    table.ForeignKey(
                        name: "fk_inventory_movements_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_inventory_movements_users_performed_by",
                        column: x => x.performed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_items", x => x.id);
                    table.CheckConstraint("ck_stock_items_location_type", "location_type IN ('warehouse','field')");
                    table.ForeignKey(
                        name: "fk_stock_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouses", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_field_inventories_environment_id_deleted_at",
                table: "field_inventories",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_field_inventories_environment_id_updated_at",
                table: "field_inventories",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_field_inventories_doctor",
                table: "field_inventories",
                column: "doctor_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inv_moves_product_time",
                table: "inventory_movements",
                columns: new[] { "environment_id", "product_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_environment_id_deleted_at",
                table: "inventory_movements",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_environment_id_updated_at",
                table: "inventory_movements",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_performed_by",
                table: "inventory_movements",
                column: "performed_by");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_product_id",
                table: "inventory_movements",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ux_inventory_movements_env_idempotency",
                table: "inventory_movements",
                columns: new[] { "environment_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_items_environment_id_deleted_at",
                table: "stock_items",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_items_environment_id_updated_at",
                table: "stock_items",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_items_product",
                table: "stock_items",
                columns: new[] { "environment_id", "product_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_items_product_id",
                table: "stock_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ux_stock_items_location_product",
                table: "stock_items",
                columns: new[] { "location_type", "location_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_environment_id_deleted_at",
                table: "warehouses",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_environment_id_updated_at",
                table: "warehouses",
                columns: new[] { "environment_id", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "field_inventories");

            migrationBuilder.DropTable(
                name: "inventory_movements");

            migrationBuilder.DropTable(
                name: "stock_items");

            migrationBuilder.DropTable(
                name: "warehouses");
        }
    }
}
