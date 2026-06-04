using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data-only: refresh-token hashing moved from BCrypt (salted — forced a verify-every-row scan)
    /// to deterministic SHA-256 matched via ix_refresh_tokens_hash. Existing BCrypt rows can never
    /// match a SHA-256 lookup, so they are dead weight — wipe them. One-time effect: every device
    /// is signed out and users log in again. No schema change.
    /// </summary>
    public partial class ResetRefreshTokensForSha256 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM refresh_tokens;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible data wipe; the rows were unmatchable under the new lookup anyway.
        }
    }
}
