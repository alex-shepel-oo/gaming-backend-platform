using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdentityService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRolePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<short>(type: "smallint", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: true),
                    permission = table.Column<string>(type: "text", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => x.id);
                    table.CheckConstraint("ck_role_permissions_platform_scope", "permission NOT LIKE 'platform.%' OR game_id IS NULL");
                    table.ForeignKey(
                        name: "fk_role_permissions_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_game_id",
                table: "role_permissions",
                column: "game_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_role_game_id",
                table: "role_permissions",
                columns: new[] { "role", "game_id" });

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_role_game_id_permission",
                table: "role_permissions",
                columns: new[] { "role", "game_id", "permission" },
                unique: true,
                filter: "game_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_role_permission_platform",
                table: "role_permissions",
                columns: new[] { "role", "permission" },
                unique: true,
                filter: "game_id IS NULL");

            // Default role_permissions rows, needed in every environment (including
            // production) - without them role_permissions is empty everywhere and even a
            // real platform admin resolves to zero perms. Game-Admin/Game-Moderator default
            // sets are per-game templates seeded where the game itself is created, not here.
            migrationBuilder.Sql(
                """
                INSERT INTO role_permissions (id, role, game_id, permission, granted_at, granted_by) VALUES
                ('00000000-0000-7000-9000-000000000001', 2, NULL, 'platform.games.manage', TIMESTAMPTZ '2026-07-24 00:00:00+00', NULL),
                ('00000000-0000-7000-9000-000000000002', 2, NULL, 'platform.currency.manage', TIMESTAMPTZ '2026-07-24 00:00:00+00', NULL),
                ('00000000-0000-7000-9000-000000000003', 2, NULL, 'platform.roles.manage', TIMESTAMPTZ '2026-07-24 00:00:00+00', NULL),
                ('00000000-0000-7000-9000-000000000004', 2, NULL, 'platform.users.read', TIMESTAMPTZ '2026-07-24 00:00:00+00', NULL),
                ('00000000-0000-7000-9000-000000000005', 2, NULL, 'platform.balance.adjust', TIMESTAMPTZ '2026-07-24 00:00:00+00', NULL),
                ('00000000-0000-7000-9000-000000000006', 2, NULL, 'game.metadata.edit', TIMESTAMPTZ '2026-07-24 00:00:00+00', NULL),
                ('00000000-0000-7000-9000-000000000007', 2, NULL, 'game.currency.manage', TIMESTAMPTZ '2026-07-24 00:00:00+00', NULL),
                ('00000000-0000-7000-9000-000000000008', 2, NULL, 'game.balance.adjust', TIMESTAMPTZ '2026-07-24 00:00:00+00', NULL),
                ('00000000-0000-7000-9000-000000000009', 2, NULL, 'game.roles.manage', TIMESTAMPTZ '2026-07-24 00:00:00+00', NULL),
                ('00000000-0000-7000-9000-00000000000a', 2, NULL, 'game.players.moderate', TIMESTAMPTZ '2026-07-24 00:00:00+00', NULL),
                ('00000000-0000-7000-9000-00000000000b', 1, NULL, 'platform.users.read', TIMESTAMPTZ '2026-07-24 00:00:00+00', NULL),
                ('00000000-0000-7000-9000-00000000000c', 1, NULL, 'game.metadata.edit', TIMESTAMPTZ '2026-07-24 00:00:00+00', NULL),
                ('00000000-0000-7000-9000-00000000000d', 1, NULL, 'game.players.moderate', TIMESTAMPTZ '2026-07-24 00:00:00+00', NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_permissions");
        }
    }
}
