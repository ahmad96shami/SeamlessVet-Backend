using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M18_Reminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications");

            migrationBuilder.AddColumn<int>(
                name: "doses_count",
                table: "prescriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "end_at",
                table: "prescriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "interval_minutes",
                table: "prescriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "last_reminded_dose",
                table: "prescriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "lead_minutes",
                table: "prescriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "reminder_enabled",
                table: "prescriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "start_at",
                table: "prescriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_reminder_active",
                table: "prescriptions",
                column: "environment_id",
                filter: "reminder_enabled = true AND deleted_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications",
                sql: "type IN ('appointment_reminder','follow_up_due','vaccination_due','medication_due','low_stock','expiry_warning','registration_request','negative_stock','account_ready_for_settlement','entitlement_approved','report_delivery')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_prescriptions_reminder_active",
                table: "prescriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "doses_count",
                table: "prescriptions");

            migrationBuilder.DropColumn(
                name: "end_at",
                table: "prescriptions");

            migrationBuilder.DropColumn(
                name: "interval_minutes",
                table: "prescriptions");

            migrationBuilder.DropColumn(
                name: "last_reminded_dose",
                table: "prescriptions");

            migrationBuilder.DropColumn(
                name: "lead_minutes",
                table: "prescriptions");

            migrationBuilder.DropColumn(
                name: "reminder_enabled",
                table: "prescriptions");

            migrationBuilder.DropColumn(
                name: "start_at",
                table: "prescriptions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications",
                sql: "type IN ('appointment_reminder','follow_up_due','vaccination_due','low_stock','expiry_warning','registration_request','negative_stock','account_ready_for_settlement','entitlement_approved','report_delivery')");
        }
    }
}
