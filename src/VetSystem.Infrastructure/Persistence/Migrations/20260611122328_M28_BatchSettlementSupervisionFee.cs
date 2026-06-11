using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M28_BatchSettlementSupervisionFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "supervision_fee",
                table: "batch_settlements",
                type: "numeric(14,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_batch_settlements_supervision_fee",
                table: "batch_settlements",
                sql: "supervision_fee >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_batch_settlements_supervision_fee",
                table: "batch_settlements");

            migrationBuilder.DropColumn(
                name: "supervision_fee",
                table: "batch_settlements");
        }
    }
}
