using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M23_BillableCareCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "billable",
                table: "prescriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "checkup_fee_visit_id",
                table: "invoice_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "night_stay_id",
                table: "invoice_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_services_system_category",
                table: "services",
                columns: new[] { "environment_id", "category" },
                unique: true,
                filter: "category IN ('checkup','night_stay') AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_checkup_fee_visit_id",
                table: "invoice_items",
                column: "checkup_fee_visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_night_stay_id",
                table: "invoice_items",
                column: "night_stay_id");

            migrationBuilder.AddForeignKey(
                name: "fk_invoice_items_night_stays_night_stay_id",
                table: "invoice_items",
                column: "night_stay_id",
                principalTable: "night_stays",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_invoice_items_visits_checkup_fee_visit_id",
                table: "invoice_items",
                column: "checkup_fee_visit_id",
                principalTable: "visits",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_invoice_items_night_stays_night_stay_id",
                table: "invoice_items");

            migrationBuilder.DropForeignKey(
                name: "fk_invoice_items_visits_checkup_fee_visit_id",
                table: "invoice_items");

            migrationBuilder.DropIndex(
                name: "ux_services_system_category",
                table: "services");

            migrationBuilder.DropIndex(
                name: "ix_invoice_items_checkup_fee_visit_id",
                table: "invoice_items");

            migrationBuilder.DropIndex(
                name: "ix_invoice_items_night_stay_id",
                table: "invoice_items");

            migrationBuilder.DropColumn(
                name: "billable",
                table: "prescriptions");

            migrationBuilder.DropColumn(
                name: "checkup_fee_visit_id",
                table: "invoice_items");

            migrationBuilder.DropColumn(
                name: "night_stay_id",
                table: "invoice_items");
        }
    }
}
