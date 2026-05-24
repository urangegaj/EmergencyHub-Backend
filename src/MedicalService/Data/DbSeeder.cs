using MedicalService.Models;
using Microsoft.EntityFrameworkCore;

namespace MedicalService.Data;

public static class DbSeeder
{
    public static readonly Guid DevSeedCityId =
        Guid.Parse("10000000-0000-0000-0000-000000000001");

    public static async Task SeedAsync(MedicalDbContext db)
    {
        if (await db.Units.AnyAsync())
            return;

        db.Units.AddRange(
            new MedicalUnit
            {
                Id = Guid.Parse("d0000001-0000-4000-8000-000000000001"),
                CityId = DevSeedCityId,
                Name = "Unit Alpha",
                Status = Models.MedicalUnitStatus.AVAILABLE
            },
            new MedicalUnit
            {
                Id = Guid.Parse("d0000002-0000-4000-8000-000000000002"),
                CityId = DevSeedCityId,
                Name = "Unit Bravo",
                Status = Models.MedicalUnitStatus.AVAILABLE
            },
            new MedicalUnit
            {
                Id = Guid.Parse("d0000003-0000-4000-8000-000000000003"),
                CityId = DevSeedCityId,
                Name = "Unit Charlie",
                Status = Models.MedicalUnitStatus.AVAILABLE
            });

        await db.SaveChangesAsync();
    }
}
