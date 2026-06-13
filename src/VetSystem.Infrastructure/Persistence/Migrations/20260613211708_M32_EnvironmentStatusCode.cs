using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M32_EnvironmentStatusCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // status: default 'active' so every pre-existing row satisfies ck_environments_status.
            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "environments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "active");

            // code is non-null + globally unique, so existing rows can't all default to "". Add it
            // nullable, backfill a unique value per row, then enforce NOT NULL + the unique index.
            migrationBuilder.AddColumn<string>(
                name: "code",
                table: "environments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // The bootstrap env gets the friendly 'BOOTSTRAP'; any other pre-existing env (e.g. a
            // crashed-test leftover) gets a deterministic unique code derived from its id (≤ 32 chars).
            migrationBuilder.Sql(
                "UPDATE environments SET code = 'BOOTSTRAP' "
                + "WHERE id = '01900000-0000-7000-8000-000000000001';");
            migrationBuilder.Sql(
                "UPDATE environments SET code = left('E' || upper(replace(id::text, '-', '')), 32) "
                + "WHERE code IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "code",
                table: "environments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_environments_code",
                table: "environments",
                column: "code",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_environments_status",
                table: "environments",
                sql: "status IN ('active','suspended')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_environments_code",
                table: "environments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_environments_status",
                table: "environments");

            migrationBuilder.DropColumn(
                name: "code",
                table: "environments");

            migrationBuilder.DropColumn(
                name: "status",
                table: "environments");
        }
    }
}
