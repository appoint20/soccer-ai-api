using System.Text.Json;
using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;
using soccer_gpt_application.Models.ML;

namespace soccer_gpt_infrastructure.Services;

public class EnhancedAnalysisService
{
    private readonly IHistoricalDataRepository _repository;
    private readonly ITeamStatsService _teamStats;
    private readonly IAdvancedStatsService _advStats;
    private readonly IMlPredictionService _mlService;
    private readonly IGeminiService _gemini; // Phase 1
    private readonly ILogger<EnhancedAnalysisService> _logger;

    public EnhancedAnalysisService(
        IHistoricalDataRepository repository,
        ITeamStatsService teamStats,
        IAdvancedStatsService advStats,
        IMlPredictionService mlService,
        IGeminiService gemini,
        ILogger<EnhancedAnalysisService> logger)
    {
        _repository = repository;
        _teamStats = teamStats;
        _advStats = advStats;
        _mlService = mlService;
        _gemini = gemini;
        _logger = logger;
    }

    public async Task<List<AnalyzedMatchDto>> RunAnalysisPipelineAsync(List<UpcomingMatchDto> fixtures)
    {
        var history = await _repository.GetAllMatchesAsync();
        var results = new List<AnalyzedMatchDto>();

        _logger.LogInformation("Starting Analysis Pipeline for {Count} fixtures...", fixtures.Count);

        // Process in parallel with semaphore or sequential? Sequential is safer for rate limits (Gemini).
        // Let's do batch of 5? No, simpler to loop.
        
        foreach (var match in fixtures)
        {
            try
            {
                // 1. Team Stats
                var homeStats = await _teamStats.CalculateStatsAsync(match.HomeTeam, history);
                var awayStats = await _teamStats.CalculateStatsAsync(match.AwayTeam, history);

                // 2. Advanced Math (Poisson, MC)
                var analytics = await _advStats.CalculateAnalyticsAsync(match.HomeTeam, match.AwayTeam, history);

                // 3. ML Prediction
                // Need to mock or construct DTOs if interfaces mismatch, but assuming passing 'match' works or mapping required
                // Assuming PredictMatchAsync accepts UpcomingMatchDto
                var historyForMl = history.Where(h => h.Date < DateTime.Parse(match.Date)).ToList();
                var mlPred = await _mlService.PredictMatchAsync(match, historyForMl);

                if (mlPred == null) continue;

                var analyzed = new AnalyzedMatchDto
                {
                    MatchId = $"{match.HomeTeam}-{match.AwayTeam}-{match.Date}",
                    HomeTeam = match.HomeTeam,
                    AwayTeam = match.AwayTeam,
                    Date = match.Date,
                    Odds = match.Odds,
                    HomeStats = homeStats,
                    AwayStats = awayStats,
                    MathProbabilities = analytics.Probabilities,
                    MonteCarlo = analytics.StreakAnalysis,
                    MlPrediction = mlPred,
                    AiPrediction = string.Empty,
                    AiReasoning = string.Empty,
                    AiConfidence = 0.0
                };

                // 4. Gemini Phase 1: Logical Thinker
                analyzed = await _gemini.AnalyzeMatchAsync(analyzed);

                results.Add(analyzed);
                _logger.LogInformation("Analyzed {Home} vs {Away}: {Pred} ({Conf:P0})", match.HomeTeam, match.AwayTeam, analyzed.AiPrediction, analyzed.AiConfidence);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing match {Home} vs {Away}", match.HomeTeam, match.AwayTeam);
            }
        }

        // 5. Store Cache
        await File.WriteAllTextAsync("analysis_cache.json", JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        
        return results;
    }
}
