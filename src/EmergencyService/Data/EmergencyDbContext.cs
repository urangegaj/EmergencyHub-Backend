using EmergencyService.Models;
using Microsoft.EntityFrameworkCore;

namespace EmergencyService.Data;

public class EmergencyDbContext(DbContextOptions<EmergencyDbContext> options) : DbContext(options)
{
    public DbSet<Emergency> Emergencies => Set<Emergency>();
    public DbSet<EmergencyType> EmergencyTypes => Set<EmergencyType>();
    public DbSet<EmergencyStatusHistory> StatusHistory => Set<EmergencyStatusHistory>();
    public DbSet<EmergencyAssignment> Assignments => Set<EmergencyAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Emergency>()
            .HasIndex(e => new { e.CityId, e.Status });

        modelBuilder.Entity<EmergencyAssignment>()
            .HasIndex(a => new { a.EmergencyId, a.DepartmentType })
            .IsUnique();
    }
}
