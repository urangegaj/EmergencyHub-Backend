using Microsoft.EntityFrameworkCore;

namespace FireService.Data;

public class FireDbContext(DbContextOptions<FireDbContext> options) : DbContext(options)
{
    // DbSets for Fire domain entities will be added here
    // e.g. public DbSet<Incident> Incidents => Set<Incident>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}
