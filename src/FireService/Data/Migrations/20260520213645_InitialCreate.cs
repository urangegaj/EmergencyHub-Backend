using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FireService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fire_cases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmergencyId = table.Column<Guid>(type: "uuid", nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AssignedUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fire_cases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "fire_units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fire_units", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_fire_cases_CityId",
                table: "fire_cases",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_fire_cases_CityId_Status",
                table: "fire_cases",
                columns: new[] { "CityId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_fire_cases_EmergencyId",
                table: "fire_cases",
                column: "EmergencyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fire_units_CityId",
                table: "fire_units",
                column: "CityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fire_cases");

            migrationBuilder.DropTable(
                name: "fire_units");
        }
    }
}
