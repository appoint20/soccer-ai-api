using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

/// <summary>
/// Service for batch analyzing matches with Gemini AI
/// </summary>
public interface IGeminiAnalysisService
{
    /// <summary>
    /// Analyze a batch of matches for a specific league
    /// </summary>
    Task<Dictionary<string, GeminiMatchAnalysis>> AnalyzeMatchBatchAsync(
        string leagueName,
        List<UpcomingMatchDto> matches,
        CancellationToken cancellationToken = default);
}
