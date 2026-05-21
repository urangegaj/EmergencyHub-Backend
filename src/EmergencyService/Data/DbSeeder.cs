using EmergencyService.Models;
using Microsoft.EntityFrameworkCore;

namespace EmergencyService.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(EmergencyDbContext db)
    {
        if (await db.EmergencyTypes.AnyAsync()) return;

        db.EmergencyTypes.AddRange(
            new EmergencyType { Name = "FIRE" },
            new EmergencyType { Name = "MEDICAL" },
            new EmergencyType { Name = "CRIME" },
            new EmergencyType { Name = "OTHER" }
        );

        await db.SaveChangesAsync();
    }
}
