using MedicalService.Models;
using Microsoft.EntityFrameworkCore;

namespace MedicalService.Data;

public class MedicalDbContext(DbContextOptions<MedicalDbContext> options) : DbContext(options)
{
    public DbSet<MedicalCase> Cases => Set<MedicalCase>();
    public DbSet<MedicalUnit> Units => Set<MedicalUnit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MedicalCase>()
            .HasIndex(c => c.EmergencyId).IsUnique();

        modelBuilder.Entity<MedicalCase>()
            .HasIndex(c => new { c.CityId, c.Status });

        modelBuilder.Entity<MedicalCase>(entity =>
        {
            entity.ToTable("medical_cases");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CityId);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.ClosedAt).HasColumnType("timestamptz");
        });

        modelBuilder.Entity<MedicalUnit>(entity =>
        {
            entity.ToTable("medical_units");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CityId);
            entity.Property(e => e.Status).HasConversion<int>();
        });
    }
}
