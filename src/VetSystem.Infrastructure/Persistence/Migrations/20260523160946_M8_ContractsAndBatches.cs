using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M8_ContractsAndBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    responsible_doctor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: true),
                    total_price = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    expected_visit_count = table.Column<int>(type: "integer", nullable: true),
                    animal_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    animal_count = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    activated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contracts", x => x.id);
                    table.CheckConstraint("ck_contracts_status", "status IN ('draft','active','completed','cancelled')");
                    table.ForeignKey(
                        name: "fk_contracts_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contracts_users_activated_by",
                        column: x => x.activated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contracts_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contracts_users_responsible_doctor_id",
                        column: x => x.responsible_doctor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    responsible_doctor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    animal_count = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    supervision_fee_model = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    supervision_fee_value = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    entitlement_enabled = table.Column<bool>(type: "boolean", nullable: true),
                    entitlement_system = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    doctor_share_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    doctor_share_ceiling = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batches", x => x.id);
                    table.CheckConstraint("ck_batches_entitlement_system", "entitlement_system IS NULL OR entitlement_system IN ('drug_profit','direct_fee')");
                    table.CheckConstraint("ck_batches_fee_model", "supervision_fee_model IN ('fixed_amount','percent_of_invoice','per_bird','per_batch_fixed')");
                    table.CheckConstraint("ck_batches_status", "status IN ('open','closed')");
                    table.ForeignKey(
                        name: "fk_batches_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batches_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batches_users_responsible_doctor_id",
                        column: x => x.responsible_doctor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_medication_prices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_price = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_medication_prices", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_medication_prices_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contract_medication_prices_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_visits_batch_id",
                table: "visits",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_visits_contract_id",
                table: "visits",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_batch_id",
                table: "invoices",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_batches_contract_id",
                table: "batches",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_batches_customer",
                table: "batches",
                columns: new[] { "environment_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_batches_customer_id",
                table: "batches",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_batches_doctor",
                table: "batches",
                columns: new[] { "environment_id", "responsible_doctor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_batches_environment_id_deleted_at",
                table: "batches",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_batches_environment_id_updated_at",
                table: "batches",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_batches_responsible_doctor_id",
                table: "batches",
                column: "responsible_doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_medication_prices_environment_id_deleted_at",
                table: "contract_medication_prices",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_medication_prices_environment_id_updated_at",
                table: "contract_medication_prices",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_medication_prices_product_id",
                table: "contract_medication_prices",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ux_contract_medication_prices_contract_product",
                table: "contract_medication_prices",
                columns: new[] { "contract_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contracts_activated_by",
                table: "contracts",
                column: "activated_by");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_created_by",
                table: "contracts",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_customer",
                table: "contracts",
                columns: new[] { "environment_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_customer_id",
                table: "contracts",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_doctor",
                table: "contracts",
                columns: new[] { "environment_id", "responsible_doctor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_environment_id_deleted_at",
                table: "contracts",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_environment_id_updated_at",
                table: "contracts",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_responsible_doctor_id",
                table: "contracts",
                column: "responsible_doctor_id");

            migrationBuilder.AddForeignKey(
                name: "fk_invoices_batches_batch_id",
                table: "invoices",
                column: "batch_id",
                principalTable: "batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_visits_batches_batch_id",
                table: "visits",
                column: "batch_id",
                principalTable: "batches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_visits_contracts_contract_id",
                table: "visits",
                column: "contract_id",
                principalTable: "contracts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_invoices_batches_batch_id",
                table: "invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_visits_batches_batch_id",
                table: "visits");

            migrationBuilder.DropForeignKey(
                name: "fk_visits_contracts_contract_id",
                table: "visits");

            migrationBuilder.DropTable(
                name: "batches");

            migrationBuilder.DropTable(
                name: "contract_medication_prices");

            migrationBuilder.DropTable(
                name: "contracts");

            migrationBuilder.DropIndex(
                name: "ix_visits_batch_id",
                table: "visits");

            migrationBuilder.DropIndex(
                name: "ix_visits_contract_id",
                table: "visits");

            migrationBuilder.DropIndex(
                name: "ix_invoices_batch_id",
                table: "invoices");
        }
    }
}
