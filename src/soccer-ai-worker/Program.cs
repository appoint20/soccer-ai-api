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

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// Mediator (some sync services publish through handlers)
var mediatorBuilder = new MediatorBuilder();
mediatorBuilder.RegisterHandlers(
    typeof(SoccerAi.Application.Features.Analysis.GetMatchAnalysisHandler).Assembly);
builder.Services.RegisterMediator(mediatorBuilder);

// Worker
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));
builder.Services.AddSingleton<SyncPipeline>();
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
return;

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
