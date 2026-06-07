using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M22_BillableVaccinations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "price",
                table: "vaccinations",
                type: "numeric(14,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "service_id",
                table: "vaccinations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "vaccination_id",
                table: "invoice_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_vaccinations_service_id",
                table: "vaccinations",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_vaccination_id",
                table: "invoice_items",
                column: "vaccination_id");

            migrationBuilder.AddForeignKey(
                name: "fk_invoice_items_vaccinations_vaccination_id",
                table: "invoice_items",
                column: "vaccination_id",
                principalTable: "vaccinations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_vaccinations_services_service_id",
                table: "vaccinations",
                column: "service_id",
                principalTable: "services",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_invoice_items_vaccinations_vaccination_id",
                table: "invoice_items");

            migrationBuilder.DropForeignKey(
                name: "fk_vaccinations_services_service_id",
                table: "vaccinations");

            migrationBuilder.DropIndex(
                name: "ix_vaccinations_service_id",
                table: "vaccinations");

            migrationBuilder.DropIndex(
                name: "ix_invoice_items_vaccination_id",
                table: "invoice_items");

            migrationBuilder.DropColumn(
                name: "price",
                table: "vaccinations");

            migrationBuilder.DropColumn(
                name: "service_id",
                table: "vaccinations");

            migrationBuilder.DropColumn(
                name: "vaccination_id",
                table: "invoice_items");
        }
    }
}
