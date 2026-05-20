using FireService.Models;
using Microsoft.EntityFrameworkCore;

namespace FireService.Data;

public class FireDbContext(DbContextOptions<FireDbContext> options) : DbContext(options)
{
    public DbSet<FireCase> Cases => Set<FireCase>();
    public DbSet<FireUnit> Units => Set<FireUnit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity()
        .HasIndex(c => c.EmergencyId).IsUnique();
        // unique constraint

        modelBuilder.Entity<FireCase>()
        .HasIndex(c => new { c.CityId, c.Status });
        //index ndb per read performance se me status ka mu bo query shpesh
        
        modelBuilder.Entity<FireCase>(entity =>
        {
            entity.ToTable("fire_cases");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CityId);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.ClosedAt).HasColumnType("timestamptz");
        });

        modelBuilder.Entity<FireUnit>(entity =>
        {
            entity.ToTable("fire_units");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CityId);
            entity.Property(e => e.Status).HasConversion<int>();
        });
    }
}
