using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M36_VisitAssignedNotification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications",
                sql: "type IN ('appointment_reminder','follow_up_due','vaccination_due','medication_due','low_stock','expiry_warning','registration_request','negative_stock','account_ready_for_settlement','entitlement_credited','report_delivery','visit_assigned')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications",
                sql: "type IN ('appointment_reminder','follow_up_due','vaccination_due','medication_due','low_stock','expiry_warning','registration_request','negative_stock','account_ready_for_settlement','entitlement_credited','report_delivery')");
        }
    }
}
