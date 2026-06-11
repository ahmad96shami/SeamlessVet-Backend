using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M26_VaccinesAsProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_vaccinations_services_service_id",
                table: "vaccinations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_category",
                table: "products");

            migrationBuilder.RenameColumn(
                name: "service_id",
                table: "vaccinations",
                newName: "product_id");

            migrationBuilder.RenameIndex(
                name: "ix_vaccinations_service_id",
                table: "vaccinations",
                newName: "ix_vaccinations_product_id");

            migrationBuilder.AddColumn<decimal>(
                name: "resolved_unit_cost",
                table: "vaccinations",
                type: "numeric(14,2)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_category",
                table: "products",
                sql: "category IN ('medication','product','vaccine')");

            // Clean cutover (no production vaccine data to preserve): the renamed product_id still
            // holds the old service FKs, which are not product ids — drop the linkage so those rows
            // become free-text records (the vaccine_type name snapshot is kept) and the new products
            // FK can be created. Retire the M22 vaccine catalog rows (soft delete — the synced
            // services table keeps them for FK integrity with any historical invoice line, but they
            // leave the reference bucket + the web catalog). The vaccine catalog is re-seeded as
            // products (DataSeeder).
            migrationBuilder.Sql("UPDATE vaccinations SET product_id = NULL;");
            migrationBuilder.Sql(
                "UPDATE services SET deleted_at = now() WHERE category = 'vaccination' AND deleted_at IS NULL;");

            migrationBuilder.AddForeignKey(
                name: "fk_vaccinations_products_product_id",
                table: "vaccinations",
                column: "product_id",
                principalTable: "products",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_vaccinations_products_product_id",
                table: "vaccinations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_category",
                table: "products");

            migrationBuilder.DropColumn(
                name: "resolved_unit_cost",
                table: "vaccinations");

            migrationBuilder.RenameColumn(
                name: "product_id",
                table: "vaccinations",
                newName: "service_id");

            migrationBuilder.RenameIndex(
                name: "ix_vaccinations_product_id",
                table: "vaccinations",
                newName: "ix_vaccinations_service_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_category",
                table: "products",
                sql: "category IN ('medication','product')");

            migrationBuilder.AddForeignKey(
                name: "fk_vaccinations_services_service_id",
                table: "vaccinations",
                column: "service_id",
                principalTable: "services",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
