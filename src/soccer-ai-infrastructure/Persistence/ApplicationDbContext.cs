using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    /// <summary>Allows provider-specific derived contexts (e.g. PostgresDbContext).</summary>
    protected ApplicationDbContext(DbContextOptions options) : base(options) { }

    public DbSet<Team> Teams { get; init; }
    public DbSet<Fixture> Fixtures { get; init; }
    public DbSet<FixtureAnalysis> FixtureAnalyses { get; init; }
    public DbSet<Combination> Combinations { get; init; }
    public DbSet<User> Users { get; init; }
    public DbSet<BacktestReport> BacktestReports { get; init; }
    public DbSet<SyncState> SyncStates { get; init; }
    public DbSet<FixtureOddsQuote> FixtureOddsQuotes { get; init; }
    public DbSet<PublishedTicket> PublishedTickets { get; init; }
    public DbSet<PublishedTicketLeg> PublishedTicketLegs { get; init; }

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
            entity.Property(c => c.Name).HasMaxLength(100);
            
            // Daily cache fields
            entity.Property(c => c.Language).HasMaxLength(5);
            
            // Unique index for the cache part only (where IsDailyCache is true)
            // SQLite doesn't support filtered indexes in EF Core easily via fluent API in old versions, 
            // so we'll just allow multiple or manage it with a composite if needed.
            // But for simplicity, we'll index Date and Language.
            entity.HasIndex(c => new { c.Date, c.Language, c.IsDailyCache });
            
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

        // ── BacktestReport ─────────────────────────────────────────────────────
        modelBuilder.Entity<BacktestReport>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => new { r.WeeksBack, r.Stake, r.CreatedAt });
            entity.ToTable("BacktestReports");
        });

        // ── FixtureOddsQuote (per-bookmaker odds history) ────────────────────
        modelBuilder.Entity<FixtureOddsQuote>(entity =>
        {
            entity.HasKey(q => q.Id);
            entity.Property(q => q.Bookmaker).HasMaxLength(60).IsRequired();
            entity.Property(q => q.Market).HasMaxLength(20).IsRequired();

            entity.HasIndex(q => new { q.FixtureId, q.Market });

            entity.HasOne<Fixture>()
                .WithMany()
                .HasForeignKey(q => q.FixtureId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("FixtureOddsQuotes");
        });

        // ── PublishedTicket (the live results ledger) ────────────────────────
        modelBuilder.Entity<PublishedTicket>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Kind).HasMaxLength(30).IsRequired();
            entity.Property(t => t.Fingerprint).HasMaxLength(64).IsRequired();
            entity.Property(t => t.Status).HasMaxLength(10).IsRequired();

            // Republishing a board must never duplicate a ticket.
            entity.HasIndex(t => t.Fingerprint).IsUnique();
            entity.HasIndex(t => new { t.BoardDateUtc, t.Status });

            entity.HasMany(t => t.Legs)
                .WithOne()
                .HasForeignKey(l => l.PublishedTicketId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable("PublishedTickets");
        });

        modelBuilder.Entity<PublishedTicketLeg>(entity =>
        {
            entity.HasKey(l => l.Id);
            entity.Property(l => l.League).HasMaxLength(100).IsRequired();
            entity.Property(l => l.Market).HasMaxLength(20).IsRequired();
            entity.Property(l => l.Selection).HasMaxLength(60).IsRequired();
            entity.Property(l => l.Status).HasMaxLength(10).IsRequired();

            entity.HasIndex(l => l.FixtureId);

            entity.ToTable("PublishedTicketLegs");
        });

        // ── SyncState (single-row operational state for the sync worker) ─────
        modelBuilder.Entity<SyncState>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.ToTable("SyncStates");
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
