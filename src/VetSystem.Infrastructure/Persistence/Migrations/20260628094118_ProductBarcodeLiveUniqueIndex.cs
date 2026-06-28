using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductBarcodeLiveUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_products_env_barcode",
                table: "products");

            // Normalize existing data so it matches the new write-path normalization (NormalizeBarcode):
            //  - blank/whitespace-only barcodes become NULL (so they no longer collide on the unique
            //    index and no longer occupy a "value"), and
            //  - surrounding whitespace is trimmed (so a scanned, trimmed code exact-matches at POS).
            // Done while the unique index is dropped, so these touch-ups can't trip a half-applied state.
            migrationBuilder.Sql(
                "UPDATE products SET barcode = NULL WHERE barcode IS NOT NULL AND btrim(barcode) = '';");
            migrationBuilder.Sql(
                "UPDATE products SET barcode = btrim(barcode) WHERE barcode IS NOT NULL AND barcode <> btrim(barcode);");

            migrationBuilder.CreateIndex(
                name: "ux_products_env_barcode",
                table: "products",
                columns: new[] { "environment_id", "barcode" },
                unique: true,
                filter: "barcode IS NOT NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_products_env_barcode",
                table: "products");

            migrationBuilder.CreateIndex(
                name: "ux_products_env_barcode",
                table: "products",
                columns: new[] { "environment_id", "barcode" },
                unique: true,
                filter: "barcode IS NOT NULL");
        }
    }
}
