using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EmergencyService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""Emergencies""
                ADD COLUMN ""SearchVector"" tsvector
                GENERATED ALWAYS AS (
                    to_tsvector('english', coalesce(""Description"", '') || ' ' || coalesce(""Address"", ''))
                ) STORED;
                CREATE INDEX ""IX_Emergencies_SearchVector"" ON ""Emergencies"" USING GIN(""SearchVector"");
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ""IX_Emergencies_SearchVector"";
                ALTER TABLE ""Emergencies"" DROP COLUMN IF EXISTS ""SearchVector"";
            ");
        }
    }
}
