using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M2_CatalogAndSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_ar = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name_latin = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    barcode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    manufacturer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    supplier = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    purchase_price = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    selling_price = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    unit_of_measure = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    expiration_date = table.Column<DateOnly>(type: "date", nullable: true),
                    reorder_point = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.CheckConstraint("ck_products_category", "category IN ('medication','product')");
                });

            migrationBuilder.CreateTable(
                name: "services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_ar = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name_latin = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    default_price = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_services", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    default_exam_fee = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    entitlement_enabled_global = table.Column<bool>(type: "boolean", nullable: false),
                    low_stock_threshold_pct = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    expiration_warning_days = table.Column<int>(type: "integer", nullable: false),
                    tax_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    tax_rate = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    logo_url = table.Column<string>(type: "text", nullable: true),
                    invoice_tax_details = table.Column<string>(type: "jsonb", nullable: true),
                    extra = table.Column<string>(type: "jsonb", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_environment_id_deleted_at",
                table: "products",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_products_environment_id_updated_at",
                table: "products",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_products_env_barcode",
                table: "products",
                columns: new[] { "environment_id", "barcode" },
                unique: true,
                filter: "barcode IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_services_environment_id_deleted_at",
                table: "services",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_services_environment_id_updated_at",
                table: "services",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_system_settings_environment_id_deleted_at",
                table: "system_settings",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_system_settings_environment_id_updated_at",
                table: "system_settings",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_system_settings_env",
                table: "system_settings",
                column: "environment_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "services");

            migrationBuilder.DropTable(
                name: "system_settings");
        }
    }
}
