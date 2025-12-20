using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_infrastructure.BackgroundServices;

/// <summary>
/// Background service that updates European fixtures weekly
/// </summary>
public class EuropeanFixturesUpdateService : BackgroundService
{
    private readonly ILogger<EuropeanFixturesUpdateService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _updateInterval = TimeSpan.FromDays(7); // Weekly
    private readonly TimeSpan _initialDelay = TimeSpan.FromMinutes(2); // Wait 2 minutes on startup

    public EuropeanFixturesUpdateService(
        ILogger<EuropeanFixturesUpdateService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("European Fixtures Update Service is starting");
            
            // Initial delay to not overwhelm startup
            await Task.Delay(_initialDelay, stoppingToken);
            
            // Run immediately on first start
            await UpdateFixturesAsync(stoppingToken);
            
            // Then run weekly
            using var timer = new PeriodicTimer(_updateInterval);
            
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await UpdateFixturesAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("European Fixtures Update Service is stopping");
        }
    }

    private async Task UpdateFixturesAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting weekly European fixtures update at {Time}", DateTime.UtcNow);
            
            // Create a scope to resolve scoped services
            using var scope = _serviceProvider.CreateScope();
            var fixturesService = scope.ServiceProvider.GetRequiredService<IEuropeanFixturesService>();
            
            var success = await fixturesService.UpdateEuropeanFixturesAsync(cancellationToken);
            
            if (success)
            {
                _logger.LogInformation("European fixtures update completed successfully at {Time}", DateTime.UtcNow);
            }
            else
            {
                _logger.LogWarning("European fixtures update completed with errors at {Time}", DateTime.UtcNow);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in European fixtures update");
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("European Fixtures Update Service is stopping");
        return base.StopAsync(cancellationToken);
    }
}
