using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdentityService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDescriptionAndIconUrlToGames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "games",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "icon_url",
                table: "games",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "description",
                table: "games");

            migrationBuilder.DropColumn(
                name: "icon_url",
                table: "games");
        }
    }
}
