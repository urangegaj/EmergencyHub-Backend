using AssessmentService.Models;
using Microsoft.EntityFrameworkCore;

namespace AssessmentService.Data;

public class AssessmentDbContext(DbContextOptions<AssessmentDbContext> options) : DbContext(options)
{
    public DbSet<AssessmentReport> Reports { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AssessmentReport>()
            .HasIndex(r => r.EmergencyId).IsUnique();
    }
}
