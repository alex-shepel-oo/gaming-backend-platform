using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EconomyService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "currencies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    scope = table.Column<short>(type: "smallint", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_currencies", x => x.id);
                    table.CheckConstraint("ck_currencies_scope_game_id", "(scope = 0 AND game_id IS NULL) OR (scope = 1 AND game_id IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "balances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_balances", x => x.id);
                    table.CheckConstraint("ck_balances_amount_non_negative", "amount >= 0");
                    table.ForeignKey(
                        name: "fk_balances_currencies_currency_id",
                        column: x => x.currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conversion_rates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversion_rates", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversion_rates_currencies_from_currency_id",
                        column: x => x.from_currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conversion_rates_currencies_to_currency_id",
                        column: x => x.to_currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    transaction_type = table.Column<short>(type: "smallint", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_ledger_entries_currencies_currency_id",
                        column: x => x.currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_balances_currency_id",
                table: "balances",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_balances_user_id_currency_id",
                table: "balances",
                columns: new[] { "user_id", "currency_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversion_rates_from_currency_id_to_currency_id",
                table: "conversion_rates",
                columns: new[] { "from_currency_id", "to_currency_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversion_rates_to_currency_id",
                table: "conversion_rates",
                column: "to_currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_currencies_code_game_id",
                table: "currencies",
                columns: new[] { "code", "game_id" },
                unique: true,
                filter: "game_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_currencies_code_platform",
                table: "currencies",
                column: "code",
                unique: true,
                filter: "game_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_currencies_game_id",
                table: "currencies",
                column: "game_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_currency_id",
                table: "ledger_entries",
                column: "currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_idempotency_key",
                table: "ledger_entries",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_user_id_currency_id",
                table: "ledger_entries",
                columns: new[] { "user_id", "currency_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "balances");

            migrationBuilder.DropTable(
                name: "conversion_rates");

            migrationBuilder.DropTable(
                name: "ledger_entries");

            migrationBuilder.DropTable(
                name: "currencies");
        }
    }
}
