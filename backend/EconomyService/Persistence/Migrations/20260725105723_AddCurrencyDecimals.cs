using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EconomyService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyDecimals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "decimals",
                table: "currencies",
                type: "smallint",
                nullable: false,
                defaultValue: (short)2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "decimals",
                table: "currencies");
        }
    }
}
