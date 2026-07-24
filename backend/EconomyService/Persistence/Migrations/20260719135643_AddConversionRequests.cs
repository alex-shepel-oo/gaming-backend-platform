using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EconomyService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversionRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversion_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_currency_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    to_amount = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false),
                    rate_applied = table.Column<decimal>(type: "numeric(20,8)", precision: 20, scale: 8, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversion_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversion_requests_currencies_from_currency_id",
                        column: x => x.from_currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conversion_requests_currencies_to_currency_id",
                        column: x => x.to_currency_id,
                        principalTable: "currencies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversion_requests_from_currency_id",
                table: "conversion_requests",
                column: "from_currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversion_requests_to_currency_id",
                table: "conversion_requests",
                column: "to_currency_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversion_requests_user_id_status",
                table: "conversion_requests",
                columns: new[] { "user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversion_requests");
        }
    }
}
