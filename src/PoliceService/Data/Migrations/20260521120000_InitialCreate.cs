using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PoliceService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "police_cases",
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
                    table.PrimaryKey("PK_police_cases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "police_units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_police_units", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_police_cases_CityId",
                table: "police_cases",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_police_cases_CityId_Status",
                table: "police_cases",
                columns: new[] { "CityId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_police_cases_EmergencyId",
                table: "police_cases",
                column: "EmergencyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_police_units_CityId",
                table: "police_units",
                column: "CityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "police_cases");

            migrationBuilder.DropTable(
                name: "police_units");
        }
    }
}
