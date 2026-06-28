using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductBarcodeNonUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_products_env_barcode",
                table: "products");

            migrationBuilder.CreateIndex(
                name: "ix_products_env_barcode",
                table: "products",
                columns: new[] { "environment_id", "barcode" },
                filter: "barcode IS NOT NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_products_env_barcode",
                table: "products");

            migrationBuilder.CreateIndex(
                name: "ux_products_env_barcode",
                table: "products",
                columns: new[] { "environment_id", "barcode" },
                unique: true,
                filter: "barcode IS NOT NULL AND deleted_at IS NULL");
        }
    }
}
