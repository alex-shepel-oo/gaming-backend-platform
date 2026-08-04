using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EconomyService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIconUrlToCurrencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "icon_url",
                table: "currencies",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "icon_url",
                table: "currencies");
        }
    }
}
