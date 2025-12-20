using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_infrastructure.BackgroundServices;

public class NightlySyncWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NightlySyncWorker> _logger;

    public NightlySyncWorker(IServiceProvider serviceProvider, ILogger<NightlySyncWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Nightly Sync Worker Started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var nextRun = now.Date.AddDays(1).AddHours(3); // 03:00 Tomorrow
            
            // If currently before 03:00, run today at 03:00
            if (now.Hour < 3)
            {
                nextRun = now.Date.AddHours(3);
            }

            var delay = nextRun - now;
            _logger.LogInformation("Next Sync scheduled for {Time} (in {Hours}h {Minutes}m)", nextRun, delay.Hours, delay.Minutes);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested) break;

            await RunSyncProcess(stoppingToken);
        }
    }
    
    // Public so it can be triggered manually via API
    public async Task RunSyncProcess(CancellationToken ct)
    {
        _logger.LogInformation("Executing Nightly Sync Pipeline...");

        using var scope = _serviceProvider.CreateScope();
        var statsService = scope.ServiceProvider.GetRequiredService<ITeamStatsSyncService>();
        var mapService = scope.ServiceProvider.GetRequiredService<ITeamMappingService>();
        var genService = scope.ServiceProvider.GetRequiredService<IFixtureGenerationService>();

        try
        {
            // Step 1: Sync Stats
            await statsService.SyncTeamStatsAsync(ct);

            // Step 2: Update Mapping
            await mapService.MapTeamsAsync(ct);

            // Step 3: Generate Fixtures (and Warm Cache)
            await genService.GenerateFixturesAsync(ct);
            
            _logger.LogInformation("Nightly Sync Pipeline Completed Successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Nightly Sync Pipeline Failed.");
        }
    }
}
