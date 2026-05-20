using FireService.Models;
using Microsoft.EntityFrameworkCore;

namespace FireService.Data;

public static class DbSeeder
{
    public static readonly Guid DevSeedCityId =
        Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    public static async Task SeedAsync(FireDbContext db)
    {
        if (await db.Units.AnyAsync())
            return;

        db.Units.AddRange(
            new FireUnit
            {
                Id = Guid.Parse("f0000001-0000-4000-8000-000000000001"),
                CityId = DevSeedCityId,
                Name = "Unit Alpha",
                Status = Models.FireUnitStatus.AVAILABLE
            },
            new FireUnit
            {
                Id = Guid.Parse("f0000002-0000-4000-8000-000000000002"),
                CityId = DevSeedCityId,
                Name = "Unit Bravo",
                Status = Models.FireUnitStatus.AVAILABLE
            },
            new FireUnit
            {
                Id = Guid.Parse("f0000003-0000-4000-8000-000000000003"),
                CityId = DevSeedCityId,
                Name = "Unit Charlie",
                Status = Models.FireUnitStatus.AVAILABLE
            });

        await db.SaveChangesAsync();
    }
}
