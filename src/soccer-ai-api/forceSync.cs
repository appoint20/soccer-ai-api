using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SoccerAi.Application.Interfaces;

public class ForceSync {
    public static async Task Sync(IServiceProvider sp) {
        using var scope = sp.CreateScope();
        var aiSync = scope.ServiceProvider.GetRequiredService<IAiSyncService>();
        Console.WriteLine("Forcing AI sync for today...");
        await aiSync.SyncUpcomingFixturesAsync(DateTime.UtcNow, force: true);
        Console.WriteLine("Done.");
    }
}
