using Microsoft.EntityFrameworkCore;

namespace SoccerAi.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL-bound context. Same model as ApplicationDbContext; exists only so
/// PostgreSQL migrations (Persistence/Migrations/Postgres) are discovered
/// separately from the legacy SQLite migrations, which stay bound to
/// ApplicationDbContext (Persistence/Migrations/SqliteLegacy).
/// </summary>
public sealed class PostgresDbContext(DbContextOptions<PostgresDbContext> options)
    : ApplicationDbContext(options);
