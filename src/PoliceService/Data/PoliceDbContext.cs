using PoliceService.Models;
using Microsoft.EntityFrameworkCore;

namespace PoliceService.Data;

public class PoliceDbContext(DbContextOptions<PoliceDbContext> options) : DbContext(options)
{
    public DbSet<PoliceCase> Cases => Set<PoliceCase>();
    public DbSet<PoliceUnit> Units => Set<PoliceUnit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PoliceCase>()
            .HasIndex(c => c.EmergencyId).IsUnique();

        modelBuilder.Entity<PoliceCase>()
            .HasIndex(c => new { c.CityId, c.Status });

        modelBuilder.Entity<PoliceCase>(entity =>
        {
            entity.ToTable("police_cases");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CityId);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.ClosedAt).HasColumnType("timestamptz");
        });

        modelBuilder.Entity<PoliceUnit>(entity =>
        {
            entity.ToTable("police_units");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CityId);
            entity.Property(e => e.Status).HasConversion<int>();
        });
    }
}
