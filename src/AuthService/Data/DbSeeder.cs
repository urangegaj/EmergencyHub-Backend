using AuthService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AuthDbContext db)
    {
        if (await db.Cities.AnyAsync()) return;

        var city = new City
        {
            Name = "Pristina",
            Region = "Prishtinë",
            Country = "Kosovo",
            IsActive = true
        };
        db.Cities.Add(city);

        var adminRole = new Role { Name = "Admin" };
        var citizenRole = new Role { Name = "Citizen" };
        var dispatcherRole = new Role { Name = "Dispatcher" };
        var responderRole = new Role { Name = "Responder" };
        db.Roles.AddRange(adminRole, citizenRole, dispatcherRole, responderRole);

        await db.SaveChangesAsync();

        var adminUser = new User
        {
            CityId = city.Id,
            Email = "admin@emergencyhub.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin1234!"),
            RoleId = adminRole.Id,
            Department = null
        };
        db.Users.Add(adminUser);

        await db.SaveChangesAsync();

        db.UserProfiles.Add(new UserProfile
        {
            UserId = adminUser.Id,
            FirstName = "System",
            LastName = "Admin",
            Phone = null
        });

        await db.SaveChangesAsync();
    }
}
