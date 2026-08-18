using SoccerAi.Application.Services.Sync;
using Mediator.Net;
using Mediator.Net.MicrosoftDependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SoccerAi.Application;
using SoccerAi.Infrastructure;
using SoccerAi.Infrastructure.Persistence;
using SoccerAi.Worker;

var builder = Host.CreateApplicationBuilder(args);

// Relative SQLite paths depend on the caller's working directory; resolve the
// real file (or --db=<path>) so the worker never creates an empty database.
ResolveSqlitePath(builder.Configuration, args);

// The worker exists to call API-Football. Without a key every request comes
// back 403 and the pipeline walks the whole league list writing nothing, so
// refuse to start rather than run a sync that cannot fetch anything.
try
{
    RequireApiFootballKey(builder.Configuration);
}
catch (InvalidOperationException ex)
{
    // Back off before exiting. A hosting platform restarts an exited worker
    // immediately, so returning here at process speed turns a misconfiguration
    // into a hot restart loop — which is how a missing key escalated into
    // exhausting the host's inotify instance limit and failing before this
    // check could even run.
    Console.Error.WriteLine($"FATAL: {ex.Message}");
    await Task.Delay(TimeSpan.FromSeconds(30));
    return 1;
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Mediator (some sync services publish through handlers)
var mediatorBuilder = new MediatorBuilder();
mediatorBuilder.RegisterHandlers(
    typeof(SoccerAi.Application.Features.Analysis.GetMatchAnalysisHandler).Assembly);
builder.Services.RegisterMediator(mediatorBuilder);

// Worker
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));
builder.Services.AddHostedService<SyncWorker>();
builder.Services.AddHostedService<OddsCaptureWorker>();

var host = builder.Build();

// Apply pending EF migrations before any worker loop touches the database.
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}

await host.RunAsync();
return 0;

static void RequireApiFootballKey(IConfigurationManager configuration)
{
    // Same resolution order the HTTP client uses, including the detail that an
    // empty environment variable does not fall through to configuration.
    var fromEnvironment = Environment.GetEnvironmentVariable("API_FOOTBALL_KEY");
    var key = fromEnvironment ?? configuration["ApiFootball:ApiKey"];

    if (!string.IsNullOrWhiteSpace(key))
        return;

    var setButBlank = fromEnvironment is not null;

    throw new InvalidOperationException(
        "API_FOOTBALL_KEY is "
        + (setButBlank ? "set but empty." : "not set.")
        + " The sync worker cannot fetch fixtures, standings or odds without it and would "
        + "otherwise run to completion having written nothing. Set API_FOOTBALL_KEY on THIS "
        + "service (the worker — the web service does not make these calls); on Render it is "
        + "declared with 'sync: false', which creates the variable but leaves the value blank "
        + "until it is filled in the dashboard.");
}

static void ResolveSqlitePath(IConfigurationManager configuration, string[] args)
{
    var provider = configuration["Database:Provider"] ?? "Sqlite";
    if (!provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        return;

    var dbOverride = args
        .FirstOrDefault(a => a.StartsWith("--db=", StringComparison.OrdinalIgnoreCase))
        ?.Split('=', 2)[1];

    if (dbOverride != null)
    {
        var full = Path.GetFullPath(dbOverride);
        configuration["ConnectionStrings:DefaultConnection"] = $"Data Source={full}";
        Console.WriteLine($"Using SQLite database: {full}");
        return;
    }

    var configured = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=data/soccer.db";
    var pathPart = configured.Split('=', 2).ElementAtOrDefault(1)?.Split(';')[0] ?? "data/soccer.db";
    if (Path.IsPathRooted(pathPart))
        return;

    var dir = Directory.GetCurrentDirectory();
    for (var i = 0; i < 6 && dir != null; i++)
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(dir, pathPart),
                     Path.Combine(dir, "src", "soccer-ai-api", pathPart),
                     Path.Combine(dir, "soccer-ai-api", pathPart)
                 })
        {
            if (File.Exists(candidate))
            {
                var full = Path.GetFullPath(candidate);
                configuration["ConnectionStrings:DefaultConnection"] = $"Data Source={full}";
                Console.WriteLine($"Using SQLite database: {full}");
                return;
            }
        }
        dir = Path.GetDirectoryName(dir);
    }

    Console.WriteLine($"WARNING: SQLite file '{pathPart}' not found near '{Directory.GetCurrentDirectory()}' — " +
                      "pass --db=<path> to point at the real database.");
}
