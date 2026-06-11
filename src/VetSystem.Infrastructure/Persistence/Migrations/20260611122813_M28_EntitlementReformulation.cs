using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M28_EntitlementReformulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ceiling_applied",
                table: "doctor_entitlements");

            migrationBuilder.DropColumn(
                name: "doctor_share_ceiling",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "doctor_share_percent",
                table: "batches");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ceiling_applied",
                table: "doctor_entitlements",
                type: "numeric(14,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "doctor_share_ceiling",
                table: "batches",
                type: "numeric(14,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "doctor_share_percent",
                table: "batches",
                type: "numeric(5,2)",
                nullable: true);
        }
    }
}
