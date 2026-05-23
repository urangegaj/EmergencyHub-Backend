using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationTransitionKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FromStatus",
                table: "notifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToStatus",
                table: "notifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_EmergencyId_Type_UserId_FromStatus_ToStatus",
                table: "notifications",
                columns: new[] { "EmergencyId", "Type", "UserId", "FromStatus", "ToStatus" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_notifications_EmergencyId_Type_UserId_FromStatus_ToStatus",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "FromStatus",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "ToStatus",
                table: "notifications");
        }
    }
}
