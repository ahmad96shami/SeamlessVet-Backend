using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PetNeutered : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_neutered",
                table: "pets",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_neutered",
                table: "pets");
        }
    }
}
