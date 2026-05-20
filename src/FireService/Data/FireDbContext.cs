using FireService.Models;
using Microsoft.EntityFrameworkCore;

namespace FireService.Data;

public class FireDbContext(DbContextOptions<FireDbContext> options) : DbContext(options)
{
    public DbSet<FireCase> Cases => Set<FireCase>();
    public DbSet<FireUnit> Units => Set<FireUnit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}
