using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SoccerAi.Application.Interfaces;
using SoccerAi.Infrastructure;
using Microsoft.Extensions.Configuration;
using System;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((hostingContext, config) =>
    {
        config.AddJsonFile("src/soccer-ai-api/appsettings.json", optional: false);
        config.AddJsonFile("src/soccer-ai-api/appsettings.Development.json", optional: true);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((hostContext, services) =>
    {
        services.AddInfrastructure(hostContext.Configuration);
    });

using var host = builder.Build();
using var scope = host.Services.CreateScope();
var fixtureSyncService = scope.ServiceProvider.GetRequiredService<IFixtureSyncService>();
var teamSyncService = scope.ServiceProvider.GetService<ITeamSyncService>();

var season = DateTime.UtcNow.Month >= 7 ? DateTime.UtcNow.Year : DateTime.UtcNow.Year - 1;
Console.WriteLine($"[Scratch] Starting sync for League 5 (National League) Season {season}");

if (teamSyncService != null)
{
    Console.WriteLine("[Scratch] Syncing standings...");
    await teamSyncService.SyncLeagueStandingsAsync(5, season, default);
}

Console.WriteLine("[Scratch] Syncing fixtures...");
await fixtureSyncService.SyncLeagueFixturesAsync(5, season, default);

Console.WriteLine("[Scratch] Sync complete for League 5.");
