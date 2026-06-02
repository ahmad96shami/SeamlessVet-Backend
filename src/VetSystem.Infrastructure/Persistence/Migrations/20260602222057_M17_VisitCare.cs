using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M17_VisitCare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_type",
                table: "ledger_entries");

            migrationBuilder.AddColumn<decimal>(
                name: "checkup_fee_applied",
                table: "visits",
                type: "numeric(14,2)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "follow_up_of_visit_id",
                table: "visits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "default_checkup_fee",
                table: "system_settings",
                type: "numeric(14,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "night_stay_id",
                table: "daily_follow_ups",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_follow_up",
                table: "appointments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "origin_visit_id",
                table: "appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "night_stays",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    care_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    check_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    check_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    nights_count = table.Column<int>(type: "integer", nullable: false),
                    nightly_rate = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    total = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_night_stays", x => x.id);
                    table.CheckConstraint("ck_night_stays_care_type", "care_type IN ('medical','icu','hotel')");
                    table.ForeignKey(
                        name: "fk_night_stays_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_visits_follow_up_of",
                table: "visits",
                columns: new[] { "environment_id", "follow_up_of_visit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_follow_up_of_visit_id",
                table: "visits",
                column: "follow_up_of_visit_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_type",
                table: "ledger_entries",
                sql: "entry_type IN ('invoice','service_fee','exam_fee','receipt_voucher','adjustment','checkup_fee','night_stay')");

            migrationBuilder.CreateIndex(
                name: "ix_followups_night_stay",
                table: "daily_follow_ups",
                column: "night_stay_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_origin_visit_id",
                table: "appointments",
                column: "origin_visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_night_stays_environment_id_deleted_at",
                table: "night_stays",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_night_stays_environment_id_updated_at",
                table: "night_stays",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_night_stays_visit",
                table: "night_stays",
                columns: new[] { "visit_id", "check_in_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_appointments_visits_origin_visit_id",
                table: "appointments",
                column: "origin_visit_id",
                principalTable: "visits",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_daily_follow_ups_night_stays_night_stay_id",
                table: "daily_follow_ups",
                column: "night_stay_id",
                principalTable: "night_stays",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_visits_visits_follow_up_of_visit_id",
                table: "visits",
                column: "follow_up_of_visit_id",
                principalTable: "visits",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_appointments_visits_origin_visit_id",
                table: "appointments");

            migrationBuilder.DropForeignKey(
                name: "fk_daily_follow_ups_night_stays_night_stay_id",
                table: "daily_follow_ups");

            migrationBuilder.DropForeignKey(
                name: "fk_visits_visits_follow_up_of_visit_id",
                table: "visits");

            migrationBuilder.DropTable(
                name: "night_stays");

            migrationBuilder.DropIndex(
                name: "ix_visits_follow_up_of",
                table: "visits");

            migrationBuilder.DropIndex(
                name: "ix_visits_follow_up_of_visit_id",
                table: "visits");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ledger_entries_type",
                table: "ledger_entries");

            migrationBuilder.DropIndex(
                name: "ix_followups_night_stay",
                table: "daily_follow_ups");

            migrationBuilder.DropIndex(
                name: "ix_appointments_origin_visit_id",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "checkup_fee_applied",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "follow_up_of_visit_id",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "default_checkup_fee",
                table: "system_settings");

            migrationBuilder.DropColumn(
                name: "night_stay_id",
                table: "daily_follow_ups");

            migrationBuilder.DropColumn(
                name: "is_follow_up",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "origin_visit_id",
                table: "appointments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ledger_entries_type",
                table: "ledger_entries",
                sql: "entry_type IN ('invoice','service_fee','exam_fee','receipt_voucher','adjustment')");
        }
    }
}
