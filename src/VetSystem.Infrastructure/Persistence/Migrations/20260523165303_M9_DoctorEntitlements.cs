using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M9_DoctorEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "doctor_entitlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    calculation_system = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    computed_amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    ceiling_applied = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_entitlements", x => x.id);
                    table.CheckConstraint("ck_entitlement_source", "(batch_id IS NOT NULL AND visit_id IS NULL) OR (batch_id IS NULL AND visit_id IS NOT NULL)");
                    table.CheckConstraint("ck_entitlements_paid_method", "paid_method IS NULL OR paid_method IN ('cash','card','bank_transfer','credit')");
                    table.CheckConstraint("ck_entitlements_status", "status IN ('pending','approved','paid')");
                    table.CheckConstraint("ck_entitlements_system", "calculation_system IN ('drug_profit','direct_fee')");
                    table.ForeignKey(
                        name: "fk_doctor_entitlements_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_doctor_entitlements_users_approved_by",
                        column: x => x.approved_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_doctor_entitlements_users_doctor_id",
                        column: x => x.doctor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_doctor_entitlements_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_entitlements_approved_by",
                table: "doctor_entitlements",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_entitlements_batch_id",
                table: "doctor_entitlements",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_entitlements_doctor_id",
                table: "doctor_entitlements",
                column: "doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_doctor_entitlements_environment_id_deleted_at",
                table: "doctor_entitlements",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_entitlements_environment_id_updated_at",
                table: "doctor_entitlements",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_entitlements_visit_id",
                table: "doctor_entitlements",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_entitlements_doctor_status",
                table: "doctor_entitlements",
                columns: new[] { "environment_id", "doctor_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "doctor_entitlements");
        }
    }
}
