using Microsoft.EntityFrameworkCore;
using NotificationService.Models;

namespace NotificationService.Data;

public class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.CityId, e.CreatedAt });
            entity.HasIndex(e => new { e.EmergencyId, e.Type });
            entity.HasIndex(e => new { e.EmergencyId, e.Type, e.UserId, e.FromStatus, e.ToStatus })
                .IsUnique();
            entity.Property(e => e.Type).HasMaxLength(64);
            entity.Property(e => e.FromStatus).HasMaxLength(64);
            entity.Property(e => e.ToStatus).HasMaxLength(64);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
        });

        modelBuilder.Entity<BackgroundJob>(entity =>
        {
            entity.ToTable("background_jobs");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Status, e.NextRetryAt });
            entity.Property(e => e.Type).HasMaxLength(64);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.NextRetryAt).HasColumnType("timestamptz");
        });
    }
}
