using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<Team> Teams { get; init; }
    public DbSet<Fixture> Fixtures { get; init; }
    public DbSet<FixtureAnalysis> FixtureAnalyses { get; init; }
    public DbSet<Combination> Combinations { get; init; }
    public DbSet<User> Users { get; init; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Team ─────────────────────────────────────────────────────────────
        modelBuilder.Entity<Team>(entity =>
        {
            entity.HasKey(t => t.Id);
            // SQLite uses Autoincrement by default for integer PKs

            entity.Property(t => t.Name).HasMaxLength(100).IsRequired();
            entity.Property(t => t.Form).HasMaxLength(10);
            
            entity.HasIndex(t => t.ApiId).IsUnique();
        });

        // ── Fixture ───────────────────────────────────────────────────────────
        modelBuilder.Entity<Fixture>(entity =>
        {
            entity.HasKey(f => f.Id);

            entity.Property(f => f.Status).HasMaxLength(10).IsRequired();

            entity.HasIndex(f => f.ApiId).IsUnique();
            entity.HasIndex(f => f.HomeTeamId);
            entity.HasIndex(f => f.AwayTeamId);

            entity.HasOne<Team>()
                .WithMany()
                .HasForeignKey(f => f.HomeTeamId)
                .HasPrincipalKey(t => t.ApiId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<Team>()
                .WithMany()
                .HasForeignKey(f => f.AwayTeamId)
                .HasPrincipalKey(t => t.ApiId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable("Fixtures");
        });

        // ── Combination ───────────────────────────────────────────────────
        modelBuilder.Entity<Combination>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).HasMaxLength(100).IsRequired();
            entity.ToTable("Combinations");
        });

        // ── FixtureAnalysis ────────────────────────────────────────────────────
        modelBuilder.Entity<FixtureAnalysis>(entity =>
        {
            entity.HasKey(a => a.Id);

            entity.Property(a => a.Lang).HasMaxLength(5).IsRequired();
            entity.Property(a => a.Recommendation).HasMaxLength(50).IsRequired();

            // One analysis per fixture per language
            entity.HasIndex(a => new { a.FixtureId, a.Lang }).IsUnique();
            
            entity.ToTable("FixtureAnalyses");
        });

        // ── User ─────────────────────────────────────────────────────────────
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Username).HasMaxLength(50).IsRequired();
            entity.Property(u => u.PasswordHash).HasMaxLength(255).IsRequired();

            entity.HasIndex(u => u.Username).IsUnique();
        });

        if (Database.IsSqlite())
        {
            var dateTimeOffsetConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, long>(
                v => v.UtcTicks,
                v => new DateTimeOffset(v, TimeSpan.Zero));

            // SQLite does not support DateTimeOffset comparisons natively.
            // Convert to long (Ticks) for SQLite only.
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var properties = entityType.ClrType.GetProperties()
                    .Where(p => p.PropertyType == typeof(DateTimeOffset) || p.PropertyType == typeof(DateTimeOffset?));

                foreach (var property in properties)
                {
                    modelBuilder.Entity(entityType.Name)
                        .Property(property.Name)
                        .HasConversion(dateTimeOffsetConverter);
                }
            }
        }
    }
}
