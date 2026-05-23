using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssessmentService.Migrations
{
    /// <inheritdoc />
    public partial class AddRetryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "Reports",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "Reports",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastError",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "Reports");
        }
    }
}
