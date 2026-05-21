using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmergenciesReplicaIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Emergencies\" REPLICA IDENTITY FULL;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Emergencies\" REPLICA IDENTITY DEFAULT;");
        }
    }
}
