using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M10_Partnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "partners",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partners", x => x.id);
                    table.ForeignKey(
                        name: "fk_partners_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "partnership_shares",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    share_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_partnership_shares", x => x.id);
                    table.CheckConstraint("ck_partnership_shares_percent", "share_percent >= 0 AND share_percent <= 100");
                    table.ForeignKey(
                        name: "fk_partnership_shares_partners_partner_id",
                        column: x => x.partner_id,
                        principalTable: "partners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_partners_environment_id_deleted_at",
                table: "partners",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_partners_environment_id_updated_at",
                table: "partners",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_partners_user_id",
                table: "partners",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_partnership_shares_environment_id_deleted_at",
                table: "partnership_shares",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_partnership_shares_environment_id_updated_at",
                table: "partnership_shares",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_partnership_shares_partner",
                table: "partnership_shares",
                columns: new[] { "environment_id", "partner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_partnership_shares_partner_id",
                table: "partnership_shares",
                column: "partner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "partnership_shares");

            migrationBuilder.DropTable(
                name: "partners");
        }
    }
}
