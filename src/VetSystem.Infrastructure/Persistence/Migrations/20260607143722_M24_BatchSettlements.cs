using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M24_BatchSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "batch_settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repricing_delta = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    original_total = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    settled_total = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    settled_by = table.Column<Guid>(type: "uuid", nullable: false),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batch_settlements", x => x.id);
                    table.CheckConstraint("ck_batch_settlements_discount", "discount_amount >= 0");
                    table.ForeignKey(
                        name: "fk_batch_settlements_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batch_settlements_users_settled_by",
                        column: x => x.settled_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "batch_settlement_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    settled_unit_price = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    original_quantity = table.Column<decimal>(type: "numeric(14,3)", nullable: false),
                    original_amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    delta = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batch_settlement_lines", x => x.id);
                    table.CheckConstraint("ck_batch_settlement_lines_price", "settled_unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_batch_settlement_lines_batch_settlements_settlement_id",
                        column: x => x.settlement_id,
                        principalTable: "batch_settlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batch_settlement_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_batch_settlement_lines_environment_id_deleted_at",
                table: "batch_settlement_lines",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_batch_settlement_lines_environment_id_updated_at",
                table: "batch_settlement_lines",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_batch_settlement_lines_product_id",
                table: "batch_settlement_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ux_batch_settlement_lines_product",
                table: "batch_settlement_lines",
                columns: new[] { "settlement_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_batch_settlements_environment_id_deleted_at",
                table: "batch_settlements",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_batch_settlements_environment_id_updated_at",
                table: "batch_settlements",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_batch_settlements_settled_by",
                table: "batch_settlements",
                column: "settled_by");

            migrationBuilder.CreateIndex(
                name: "ux_batch_settlements_batch",
                table: "batch_settlements",
                column: "batch_id",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_batch_settlements_env_idempotency",
                table: "batch_settlements",
                columns: new[] { "environment_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "batch_settlement_lines");

            migrationBuilder.DropTable(
                name: "batch_settlements");
        }
    }
}
