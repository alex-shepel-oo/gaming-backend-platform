using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdentityService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScopeToRefreshTokenFamilies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "scope",
                table: "refresh_token_families",
                type: "smallint",
                nullable: true);

            // Existing rows predate the account/platform distinction, but their scope is
            // still mechanically derivable from the same GameId check LoginAsync and
            // RotateAsync used before this column existed: null game_id meant Platform,
            // a real game_id meant Game (account-scoped families did not exist yet).
            migrationBuilder.Sql(
                """
                UPDATE refresh_token_families
                SET scope = CASE WHEN game_id IS NULL THEN 2 ELSE 1 END;
                """);

            migrationBuilder.AlterColumn<short>(
                name: "scope",
                table: "refresh_token_families",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "scope",
                table: "refresh_token_families");
        }
    }
}
