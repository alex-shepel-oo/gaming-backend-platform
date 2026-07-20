using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EconomyService.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectedEventCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "projected_event_counts",
                columns: table => new
                {
                    event_type = table.Column<string>(type: "text", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projected_event_counts", x => x.event_type);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "projected_event_counts");
        }
    }
}
