using Microsoft.EntityFrameworkCore;
using SoccerAi.Infrastructure.Persistence;

namespace SoccerAi.Api.Startup;

/// <summary>
/// Brings the database up at boot, and fails with an explanation rather than a
/// provider error code when it cannot.
///
/// The failure this exists to prevent: a misconfigured deployment silently
/// falling back to SQLite, then dying on "SQLite Error 14: unable to open
/// database file". That message names neither the path it tried nor the reason
/// the wrong provider was chosen, which is most of the work in diagnosing it.
/// </summary>
public static class DatabaseStartup
{
    public static void MigrateAndReport(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        var provider = app.Configuration["Database:Provider"] ?? "Sqlite";
        var isSqlite = !provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase);

        logger.LogInformation("[Startup] Database provider: {Provider}", provider);

        // A container filesystem is wiped on every deploy, so SQLite there loses
        // every result the moment the service restarts. Say so loudly rather
        // than discovering it after a week of collected picks disappears.
        if (isSqlite && app.Environment.IsProduction())
        {
            logger.LogWarning(
                "[Startup] SQLite in Production. Unless the file sits on a mounted persistent "
                + "disk, ALL DATA IS LOST ON EVERY DEPLOY. Set Database__Provider=Postgres and "
                + "ConnectionStrings__PostgresConnection to use the managed database.");
        }

        if (isSqlite) EnsureSqliteDirectoryExists(app.Configuration, logger);

        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        try
        {
            context.Database.Migrate();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Database migration failed. Provider='{provider}', "
                + $"DataSource='{Describe(context)}'. "
                + (isSqlite
                    ? "The directory must exist and be writable by the container user. On a "
                      + "hosted platform, prefer Postgres: set Database__Provider=Postgres."
                    : "Check that ConnectionStrings__PostgresConnection is set and reachable."),
                ex);
        }

        Report(context, logger, isSqlite);
    }

    /// <summary>
    /// SQLite will not create a missing directory, and reports the failure as a
    /// generic "unable to open database file". Creating it removes the whole
    /// failure class, locally and in a container.
    /// </summary>
    private static void EnsureSqliteDirectoryExists(IConfiguration configuration, ILogger logger)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        try
        {
            var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
            var fullPath = Path.GetFullPath(builder.DataSource);
            var directory = Path.GetDirectoryName(fullPath);

            logger.LogInformation("[Startup] SQLite file: {Path}", fullPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                logger.LogInformation("[Startup] Created missing directory {Directory}", directory);
            }
        }
        catch (Exception ex)
        {
            // Never block startup on this: the migration below reports properly.
            logger.LogWarning(ex, "[Startup] Could not prepare the SQLite directory.");
        }
    }

    private static string Describe(DbContext context)
    {
        try
        {
            var connection = context.Database.GetDbConnection();
            return string.IsNullOrEmpty(connection.DataSource) ? "(none)" : connection.DataSource;
        }
        catch
        {
            return "(unavailable)";
        }
    }

    /// <summary>
    /// An empty database on a fresh deployment is the single most likely reason
    /// the API returns nothing, so it is worth stating at boot rather than
    /// leaving someone to infer it from empty responses.
    /// </summary>
    private static void Report(ApplicationDbContext context, ILogger logger, bool isSqlite)
    {
        try
        {
            var fixtures = context.Fixtures.Count();
            var teams = context.Teams.Count();

            logger.LogInformation("[Startup] Connected: {Fixtures} fixtures, {Teams} teams", fixtures, teams);

            if (fixtures == 0)
            {
                logger.LogWarning(
                    "[Startup] The database is EMPTY. Endpoints will return no matches until data "
                    + "is loaded — run the sync worker, or migrate an existing SQLite file with "
                    + "'soccer-ai-tools migrate-data'.{Hint}",
                    isSqlite ? " Note that .db files are gitignored and are not in the image." : "");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Startup] Could not read table counts.");
        }
    }
}
