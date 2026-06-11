using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M30_EntitlementCollapse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // M30 — entitlements are batch-only now. Any pre-existing visit-sourced rows (batch_id NULL)
            // are obsolete (the per-visit System-B entitlement is removed); delete them before batch_id
            // becomes NOT NULL so the alter does not backfill them with a zero GUID.
            migrationBuilder.Sql("DELETE FROM doctor_entitlements WHERE batch_id IS NULL;");

            migrationBuilder.DropForeignKey(
                name: "fk_doctor_entitlements_users_approved_by",
                table: "doctor_entitlements");

            migrationBuilder.DropForeignKey(
                name: "fk_doctor_entitlements_visits_visit_id",
                table: "doctor_entitlements");

            migrationBuilder.DropIndex(
                name: "ix_doctor_entitlements_approved_by",
                table: "doctor_entitlements");

            migrationBuilder.DropIndex(
                name: "ix_doctor_entitlements_visit_id",
                table: "doctor_entitlements");

            migrationBuilder.DropIndex(
                name: "ix_entitlements_doctor_status",
                table: "doctor_entitlements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_entitlement_source",
                table: "doctor_entitlements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_entitlements_paid_method",
                table: "doctor_entitlements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_entitlements_status",
                table: "doctor_entitlements");

            migrationBuilder.DropColumn(
                name: "approved_at",
                table: "doctor_entitlements");

            migrationBuilder.DropColumn(
                name: "approved_by",
                table: "doctor_entitlements");

            migrationBuilder.DropColumn(
                name: "paid_at",
                table: "doctor_entitlements");

            migrationBuilder.DropColumn(
                name: "paid_method",
                table: "doctor_entitlements");

            migrationBuilder.DropColumn(
                name: "status",
                table: "doctor_entitlements");

            migrationBuilder.DropColumn(
                name: "visit_id",
                table: "doctor_entitlements");

            migrationBuilder.AlterColumn<Guid>(
                name: "batch_id",
                table: "doctor_entitlements",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_entitlements_doctor",
                table: "doctor_entitlements",
                columns: new[] { "environment_id", "doctor_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_entitlements_doctor",
                table: "doctor_entitlements");

            migrationBuilder.AlterColumn<Guid>(
                name: "batch_id",
                table: "doctor_entitlements",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                table: "doctor_entitlements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "approved_by",
                table: "doctor_entitlements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "paid_at",
                table: "doctor_entitlements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "paid_method",
                table: "doctor_entitlements",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "doctor_entitlements",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "visit_id",
                table: "doctor_entitlements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_doctor_entitlements_approved_by",
                table: "doctor_entitlements",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_entitlements_visit_id",
                table: "doctor_entitlements",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_entitlements_doctor_status",
                table: "doctor_entitlements",
                columns: new[] { "environment_id", "doctor_id", "status" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_entitlement_source",
                table: "doctor_entitlements",
                sql: "(batch_id IS NOT NULL AND visit_id IS NULL) OR (batch_id IS NULL AND visit_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_entitlements_paid_method",
                table: "doctor_entitlements",
                sql: "paid_method IS NULL OR paid_method IN ('cash','card','bank_transfer','credit')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_entitlements_status",
                table: "doctor_entitlements",
                sql: "status IN ('pending','approved','paid')");

            migrationBuilder.AddForeignKey(
                name: "fk_doctor_entitlements_users_approved_by",
                table: "doctor_entitlements",
                column: "approved_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_doctor_entitlements_visits_visit_id",
                table: "doctor_entitlements",
                column: "visit_id",
                principalTable: "visits",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
