using Microsoft.AspNetCore.Mvc;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AutomationController : ControllerBase
{
    private readonly ILogger<AutomationController> _logger;
    private readonly ITeamStatsSyncService _statsService;
    private readonly ITeamMappingService _mappingService;
    private readonly IFixtureGenerationService _fixtureService;

    public AutomationController(
        ILogger<AutomationController> logger,
        ITeamStatsSyncService statsService,
        ITeamMappingService mappingService,
        IFixtureGenerationService fixtureService)
    {
        _logger = logger;
        _statsService = statsService;
        _mappingService = mappingService;
        _fixtureService = fixtureService;
    }

    [HttpPost("run-sync")]
    public async Task<IActionResult> RunFullSync(CancellationToken ct)
    {
        _logger.LogInformation("Manual Sync Triggered via API");

        try
        {
            // Step 1
            await _statsService.SyncTeamStatsAsync(ct);
            // Step 2
            await _mappingService.MapTeamsAsync(ct);
            // Step 3
            await _fixtureService.GenerateFixturesAsync(ct);

            return Ok(new { Message = "Sync Completed Successfully. check logs and data folder." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual Sync Failed");
            return StatusCode(500, new { Message = "Sync Failed", Error = ex.Message });
        }
    }

    [HttpGet("backtest")]
    public async Task<IActionResult> RunBacktest([FromServices] soccer_gpt_infrastructure.Services.Analysis.MlBacktestService backtester, [FromQuery] int weeks = 10)
    {
        _logger.LogInformation("Starting Backtest...");
        try
        {
            var report = await backtester.RunBacktestAsync(weeks);
            return Ok(report);
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Backtest Failed");
             return StatusCode(500, ex.Message);
        }
    }

    [HttpPost("run-gemini-backtest")]
    public async Task<IActionResult> RunGeminiBacktest(
        [FromServices] soccer_gpt_infrastructure.Services.Analysis.GeminiBacktestService backtestService,
        [FromQuery] int weeks = 15,
        [FromQuery] int maxMatches = 4)
    {
        var report = await backtestService.RunBacktestAsync(weeks, maxMatches);
        return Ok(report);
    }

    [HttpPost("run-failure-analysis")]
    public async Task<IActionResult> RunFailureAnalysis(
        [FromServices] soccer_gpt_infrastructure.Services.Analysis.GeminiBacktestService backtestService,
        [FromQuery] int weeks = 15)
    {
        var report = await backtestService.RunFailureAnalysisAsync(weeks);
        return Ok(report);
    }

    [HttpPost("run-low-score-analysis")]
    public async Task<IActionResult> RunLowScoreAnalysis(
        [FromServices] soccer_gpt_infrastructure.Services.Analysis.GeminiBacktestService backtestService,
        [FromQuery] int weeks = 15)
    {
        var report = await backtestService.RunLowScoreAnalysisAsync(weeks);
        return Ok(report);
    }

    [HttpGet("backtest/accumulator")]
    public async Task<IActionResult> RunAccumulatorBacktest(
        [FromServices] soccer_gpt_infrastructure.Services.Analysis.MlBacktestService backtester, 
        [FromQuery] int weeks = 10,
        [FromQuery] double minOdds = 1.77)
    {
        _logger.LogInformation("Starting Accumulator Backtest...");
        try
        {
            var report = await backtester.RunAccumulatorBacktestAsync(weeks, minOdds, 3);
            return Ok(report);
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Backtest Failed");
             return StatusCode(500, ex.Message);
        }
    }
        [HttpGet("backtest/portfolio")]
    public async Task<IActionResult> RunDailyPortfolioBacktest(
        [FromServices] soccer_gpt_infrastructure.Services.Analysis.MlBacktestService backtester, 
        [FromQuery] int weeks = 10,
        [FromQuery] double minOdds = 1.77)
    {
        _logger.LogInformation("Starting Portfolio Backtest...");
        try
        {
            var report = await backtester.RunDailyPortfolioBacktestAsync(weeks, minOdds);
            return Ok(report);
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Backtest Failed");
             return StatusCode(500, ex.Message);
        }
    }
    [HttpGet("backtest/weekly")]
    public async Task<IActionResult> RunWeeklyPortfolioBacktest(
        [FromServices] soccer_gpt_infrastructure.Services.Analysis.MlBacktestService backtester, 
        [FromQuery] int weeks = 15,
        [FromQuery] double minOdds = 1.40)
    {
        _logger.LogInformation("Starting Weekly Portfolio Backtest...");
        try
        {
            var report = await backtester.RunWeeklyPortfolioBacktestAsync(weeks, minOdds);
            return Ok(report);
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "Backtest Failed");
             return StatusCode(500, ex.Message);
        }
    }
}

