using PoliceService.Models;
using Microsoft.EntityFrameworkCore;

namespace PoliceService.Data;

public static class DbSeeder
{
    public static readonly Guid DevSeedCityId =
        Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    public static async Task SeedAsync(PoliceDbContext db)
    {
        if (await db.Units.AnyAsync())
            return;

        db.Units.AddRange(
            new PoliceUnit
            {
                Id = Guid.Parse("b0000001-0000-4000-8000-000000000001"),
                CityId = DevSeedCityId,
                Name = "Unit Alpha",
                Status = Models.PoliceUnitStatus.AVAILABLE
            },
            new PoliceUnit
            {
                Id = Guid.Parse("b0000002-0000-4000-8000-000000000002"),
                CityId = DevSeedCityId,
                Name = "Unit Bravo",
                Status = Models.PoliceUnitStatus.AVAILABLE
            },
            new PoliceUnit
            {
                Id = Guid.Parse("b0000003-0000-4000-8000-000000000003"),
                CityId = DevSeedCityId,
                Name = "Unit Charlie",
                Status = Models.PoliceUnitStatus.AVAILABLE
            });

        await db.SaveChangesAsync();
    }
}
