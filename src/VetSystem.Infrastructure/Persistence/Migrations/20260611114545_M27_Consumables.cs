using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M27_Consumables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_movements_type",
                table: "inventory_movements");

            migrationBuilder.AddColumn<bool>(
                name: "is_consumable",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost",
                table: "inventory_movements",
                type: "numeric(14,2)",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_movements_type",
                table: "inventory_movements",
                sql: "movement_type IN ('receive','adjust','load_to_field','unload_from_field','sale_deduct','return_add','consume')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_inventory_movements_type",
                table: "inventory_movements");

            migrationBuilder.DropColumn(
                name: "is_consumable",
                table: "products");

            migrationBuilder.DropColumn(
                name: "unit_cost",
                table: "inventory_movements");

            migrationBuilder.AddCheckConstraint(
                name: "ck_inventory_movements_type",
                table: "inventory_movements",
                sql: "movement_type IN ('receive','adjust','load_to_field','unload_from_field','sale_deduct','return_add')");
        }
    }
}
