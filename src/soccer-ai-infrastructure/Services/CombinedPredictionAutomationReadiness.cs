using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Options;
using SoccerAi.Infrastructure.Options;

namespace SoccerAi.Infrastructure.Services;

public sealed class CombinedPredictionAutomationReadiness(IApplicationDbContext db, IConfiguration configuration,
    IOptions<CombinedPredictionAutomationOptions> automation, IOptions<OpenRouterOptions> router,
    IOptions<AiServiceOptions> narration, IOptions<HybridModelOptions> hybrid)
    : ICombinedPredictionAutomationReadiness
{
    public async Task<string?> BlockingReasonAsync(CancellationToken ct)
    {
        var footballKey = Environment.GetEnvironmentVariable("API_FOOTBALL_KEY") ?? configuration["ApiFootball:ApiKey"];
        if (string.IsNullOrWhiteSpace(footballKey)) return "API-Football credentials are missing.";
        if (!router.Value.Enabled || string.IsNullOrWhiteSpace(router.Value.ApiKey)
            || OpenAiAnalysisService.DescribeKeyProblem(router.Value.BaseUrl, router.Value.ApiKey) is not null)
            return "Numerical AI is disabled or its credentials are missing/invalid.";
        var key = AiCredentials.Resolve(configuration, narration.Value.ApiKey, narration.Value.BaseUrl);
        if (!narration.Value.Enabled || string.IsNullOrWhiteSpace(key)
            || OpenAiAnalysisService.DescribeKeyProblem(narration.Value.BaseUrl, key) is not null)
            return "Narration is disabled or its credentials are missing/invalid.";
        if (string.IsNullOrWhiteSpace(narration.Value.DefaultModel) || !narration.Value.DefaultModel.EndsWith(":free", StringComparison.Ordinal)
            || (!string.IsNullOrWhiteSpace(narration.Value.FallbackModel)
                && !narration.Value.FallbackModel.EndsWith(":free", StringComparison.Ordinal)))
            return "Scheduled narration requires explicit free primary/fallback models.";
        if (automation.Value.RequireAcceptedMl && (!hybrid.Value.Enabled || !await db.GoalRateModelGenerations.AnyAsync(ct)))
            return "No enabled, accepted ML generation is available in shared storage.";
        return null;
    }
}
