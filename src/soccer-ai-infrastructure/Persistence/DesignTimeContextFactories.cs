using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SoccerAi.Infrastructure.Persistence;

// Schema generation needs no production settings or startup host. Neither
// factory connects to a database when scaffolding migrations or SQL scripts.
public sealed class SqliteDesignTimeContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite("Data Source=:memory:").Options);
}

public sealed class PostgresDesignTimeContextFactory : IDesignTimeDbContextFactory<PostgresDbContext>
{
    public PostgresDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<PostgresDbContext>()
        .UseNpgsql("Host=localhost;Database=soccer_ai_design;Username=design").Options);
}
