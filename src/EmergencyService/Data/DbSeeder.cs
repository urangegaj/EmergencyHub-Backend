using EmergencyService.Models;
using Microsoft.EntityFrameworkCore;

namespace EmergencyService.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(EmergencyDbContext db)
    {
        if (await db.EmergencyTypes.AnyAsync()) return;

        db.EmergencyTypes.AddRange(
            new EmergencyType { Id = Guid.Parse("20000000-0000-0000-0000-000000000001"), Name = "FIRE" },
            new EmergencyType { Id = Guid.Parse("20000000-0000-0000-0000-000000000002"), Name = "MEDICAL" },
            new EmergencyType { Id = Guid.Parse("20000000-0000-0000-0000-000000000003"), Name = "CRIME" },
            new EmergencyType { Id = Guid.Parse("20000000-0000-0000-0000-000000000004"), Name = "OTHER" }
        );

        await db.SaveChangesAsync();
    }
}
