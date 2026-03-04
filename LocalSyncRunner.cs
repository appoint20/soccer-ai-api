using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using SoccerAi.Infrastructure;
using SoccerAi.Application.Interfaces;
using Microsoft.Extensions.Logging;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("src/soccer-ai-api/appsettings.json", optional: true)
    .AddJsonFile("src/soccer-ai-api/appsettings.Development.json", optional: true)
    .AddUserSecrets("714295b9-82ec-4a8b-ae72-6c59a8c87cf2") // The ID I found earlier
    .Build();

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole());
services.AddInfrastructure(configuration);

var provider = services.BuildServiceProvider();
var runner = provider.GetRequiredService<ISyncJobRunner>();

Console.WriteLine("Starting Local Bulk Sync for last 2 seasons...");
try 
{
    // Sync matches and standings
    await runner.SyncMatchesAsync();
    await runner.SyncStandingsAsync();
    
    // Also trigger the new multi-season logic I added
    var fixtureService = provider.GetRequiredService<IFixtureSyncService>();
    Console.WriteLine("Syncing last 2 seasons for all leagues...");
    await fixtureService.SyncMultipleSeasonsAsync(2, CancellationToken.None);
    
    Console.WriteLine("Sync completed successfully!");
}
catch (Exception ex)
{
    Console.WriteLine($"Sync failed: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
