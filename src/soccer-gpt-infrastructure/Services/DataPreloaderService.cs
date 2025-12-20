using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_infrastructure.Services;

public class DataPreloaderService : BackgroundService
{
    private readonly IHistoricalDataRepository _repository;
    private readonly ILogger<DataPreloaderService> _logger;

    public DataPreloaderService(
        IHistoricalDataRepository repository,
        ILogger<DataPreloaderService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Creating Excel Data Cache...");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // This triggers the lazy loading in the repository
            var count = (await _repository.GetAllMatchesAsync()).Count;
            
            stopwatch.Stop();
            _logger.LogInformation("Data Cache Created. Loaded {Count} matches in {Elapsed}ms.", count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to preload data cache.");
        }
    }
}
