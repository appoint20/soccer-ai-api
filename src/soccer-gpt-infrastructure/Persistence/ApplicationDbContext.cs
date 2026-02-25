using Microsoft.EntityFrameworkCore;
using soccer_gpt_application.Entities;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<Team> Teams { get; set; }
    public DbSet<Fixture> Fixtures { get; set; }
    public DbSet<UserCombination> UserCombinations { get; set; }
    public DbSet<UserCombinationMatch> UserCombinationMatches { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Team>()
            .HasIndex(t => t.ApiId)
            .IsUnique();

        modelBuilder.Entity<Fixture>()
            .HasIndex(f => f.ApiId)
            .IsUnique();

        modelBuilder.Entity<Fixture>()
            .HasOne<Team>()
            .WithMany()
            .HasForeignKey(f => f.HomeTeamId)
            .HasPrincipalKey(t => t.ApiId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Fixture>()
            .HasOne<Team>()
            .WithMany()
            .HasForeignKey(f => f.AwayTeamId)
            .HasPrincipalKey(t => t.ApiId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Fixture>()
            .ToTable(t => t.HasCheckConstraint("CK_Fixtures_HomeAwayDifferent", "\"HomeTeamId\" <> \"AwayTeamId\""));

        modelBuilder.Entity<UserCombination>()
            .HasMany(uc => uc.Matches)
            .WithOne(m => m.UserCombination)
            .HasForeignKey(m => m.UserCombinationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
