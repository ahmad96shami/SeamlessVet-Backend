using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M34_RefreshTokenHashUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_hash",
                table: "refresh_tokens");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_refresh_tokens_hash",
                table: "refresh_tokens");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_hash",
                table: "refresh_tokens",
                column: "token_hash");
        }
    }
}
