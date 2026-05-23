using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M5_VisitsAndMedical : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "visits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    visit_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receptionist_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    chief_complaint = table.Column<string>(type: "text", nullable: true),
                    symptoms = table.Column<string>(type: "text", nullable: true),
                    temperature = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    heart_rate = table.Column<int>(type: "integer", nullable: true),
                    respiratory_rate = table.Column<int>(type: "integer", nullable: true),
                    weight = table.Column<decimal>(type: "numeric(8,3)", nullable: true),
                    clinical_notes = table.Column<string>(type: "text", nullable: true),
                    preliminary_diagnosis = table.Column<string>(type: "text", nullable: true),
                    final_diagnosis = table.Column<string>(type: "text", nullable: true),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    icd_vet_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    exam_fee_applied = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visits", x => x.id);
                    table.CheckConstraint("ck_visits_severity", "severity IS NULL OR severity IN ('mild','moderate','severe','critical')");
                    table.CheckConstraint("ck_visits_status", "status IN ('open','in_progress','completed','cancelled')");
                    table.CheckConstraint("ck_visits_type", "visit_type IN ('in_clinic','field')");
                    table.ForeignKey(
                        name: "fk_visits_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_visits_pets_pet_id",
                        column: x => x.pet_id,
                        principalTable: "pets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_visits_users_doctor_id",
                        column: x => x.doctor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_visits_users_receptionist_id",
                        column: x => x.receptionist_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    url = table.Column<string>(type: "text", nullable: true),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    doc_date = table.Column<DateOnly>(type: "date", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    upload_status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachments", x => x.id);
                    table.CheckConstraint("ck_attachments_file_type", "file_type IN ('photo','pdf')");
                    table.CheckConstraint("ck_attachments_upload_status", "upload_status IN ('pending','uploaded','failed')");
                    table.ForeignKey(
                        name: "fk_attachments_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "daily_follow_ups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    condition = table.Column<string>(type: "text", nullable: true),
                    temperature = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    heart_rate = table.Column<int>(type: "integer", nullable: true),
                    respiratory_rate = table.Column<int>(type: "integer", nullable: true),
                    administered_meds = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_follow_ups", x => x.id);
                    table.ForeignKey(
                        name: "fk_daily_follow_ups_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "prescriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dosage = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    frequency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    duration = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    dispense_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(14,3)", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prescriptions", x => x.id);
                    table.CheckConstraint("ck_prescriptions_dispense_type", "dispense_type IN ('administered_in_clinic','dispensed_to_owner')");
                    table.ForeignKey(
                        name: "fk_prescriptions_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_prescriptions_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "procedures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_text = table.Column<string>(type: "text", nullable: true),
                    result_file_url = table.Column<string>(type: "text", nullable: true),
                    price = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_procedures", x => x.id);
                    table.ForeignKey(
                        name: "fk_procedures_services_service_id",
                        column: x => x.service_id,
                        principalTable: "services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_procedures_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vaccinations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pet_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    visit_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vaccine_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    date_given = table.Column<DateOnly>(type: "date", nullable: false),
                    next_due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    certificate_url = table.Column<string>(type: "text", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vaccinations", x => x.id);
                    table.ForeignKey(
                        name: "fk_vaccinations_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vaccinations_pets_pet_id",
                        column: x => x.pet_id,
                        principalTable: "pets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vaccinations_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_visit_id",
                table: "inventory_movements",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_environment_id_deleted_at",
                table: "attachments",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_environment_id_updated_at",
                table: "attachments",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_visit",
                table: "attachments",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_daily_follow_ups_environment_id_deleted_at",
                table: "daily_follow_ups",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_follow_ups_environment_id_updated_at",
                table: "daily_follow_ups",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_followups_visit",
                table: "daily_follow_ups",
                columns: new[] { "visit_id", "entry_date" });

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_environment_id_deleted_at",
                table: "prescriptions",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_environment_id_updated_at",
                table: "prescriptions",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_product_id",
                table: "prescriptions",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_prescriptions_visit",
                table: "prescriptions",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_procedures_environment_id_deleted_at",
                table: "procedures",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_procedures_environment_id_updated_at",
                table: "procedures",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_procedures_service_id",
                table: "procedures",
                column: "service_id");

            migrationBuilder.CreateIndex(
                name: "ix_procedures_visit",
                table: "procedures",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_vaccinations_customer_id",
                table: "vaccinations",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_vaccinations_due",
                table: "vaccinations",
                columns: new[] { "environment_id", "next_due_date" },
                filter: "next_due_date IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_vaccinations_environment_id_deleted_at",
                table: "vaccinations",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_vaccinations_environment_id_updated_at",
                table: "vaccinations",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_vaccinations_pet_id",
                table: "vaccinations",
                column: "pet_id");

            migrationBuilder.CreateIndex(
                name: "ix_vaccinations_visit_id",
                table: "vaccinations",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_visits_batch",
                table: "visits",
                columns: new[] { "environment_id", "batch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_contract",
                table: "visits",
                columns: new[] { "environment_id", "contract_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_customer_id",
                table: "visits",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_visits_customer_time",
                table: "visits",
                columns: new[] { "environment_id", "customer_id", "started_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_visits_doctor_id",
                table: "visits",
                column: "doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_visits_doctor_time",
                table: "visits",
                columns: new[] { "environment_id", "doctor_id", "started_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_visits_environment_id_deleted_at",
                table: "visits",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_environment_id_updated_at",
                table: "visits",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_pet_id",
                table: "visits",
                column: "pet_id");

            migrationBuilder.CreateIndex(
                name: "ix_visits_receptionist_id",
                table: "visits",
                column: "receptionist_id");

            migrationBuilder.CreateIndex(
                name: "ux_visits_env_number",
                table: "visits",
                columns: new[] { "environment_id", "visit_number" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_inventory_movements_visits_visit_id",
                table: "inventory_movements",
                column: "visit_id",
                principalTable: "visits",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_inventory_movements_visits_visit_id",
                table: "inventory_movements");

            migrationBuilder.DropTable(
                name: "attachments");

            migrationBuilder.DropTable(
                name: "daily_follow_ups");

            migrationBuilder.DropTable(
                name: "prescriptions");

            migrationBuilder.DropTable(
                name: "procedures");

            migrationBuilder.DropTable(
                name: "vaccinations");

            migrationBuilder.DropTable(
                name: "visits");

            migrationBuilder.DropIndex(
                name: "ix_inventory_movements_visit_id",
                table: "inventory_movements");
        }
    }
}
