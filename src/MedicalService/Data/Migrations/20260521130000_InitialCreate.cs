using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "medical_cases",
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
                    table.PrimaryKey("PK_medical_cases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "medical_units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_medical_units", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_medical_cases_CityId",
                table: "medical_cases",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_medical_cases_CityId_Status",
                table: "medical_cases",
                columns: new[] { "CityId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_medical_cases_EmergencyId",
                table: "medical_cases",
                column: "EmergencyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_medical_units_CityId",
                table: "medical_units",
                column: "CityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "medical_cases");

            migrationBuilder.DropTable(
                name: "medical_units");
        }
    }
}
