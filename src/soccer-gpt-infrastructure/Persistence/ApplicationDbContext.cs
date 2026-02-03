using Microsoft.EntityFrameworkCore;
using soccer_gpt_application.Entities;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<Team> Teams { get; set; }
    public DbSet<Fixture> Fixtures { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Team>()
            .HasIndex(t => t.ApiId)
            .IsUnique();

        modelBuilder.Entity<Fixture>()
            .HasIndex(f => f.ApiId)
            .IsUnique();
    }
}

