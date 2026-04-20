using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Features.Combinations;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Entities;

namespace SoccerAi.Application.Services.Combinations;

public sealed class ChatCombinationEngine(
    IAiAnalysisService aiService,
    IApplicationDbContext dbContext) : IChatCombinationEngine
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<List<CombinationDto>> GenerateCombinationsAsync(List<MatchAnalysis> matches, ChatCombinationIntent intent)
    {
        // 1. Handle SYSTEM caching
        if (intent.SourceType == "SYSTEM" && !intent.Refresh)
        {
            var cached = await dbContext.Combinations
                .FirstOrDefaultAsync(c => c.Date == matches.FirstOrDefault().Date.Date && 
                                         c.Language == "en" && 
                                         c.IsDailyCache);

            if (cached != null && !string.IsNullOrWhiteSpace(cached.Payload))
            {
                try
                {
                    return System.Text.Json.JsonSerializer.Deserialize<List<CombinationDto>>(cached.Payload, JsonOpts) ?? [];
                }
                catch { /* Fallback to generation if corrupted */ }
            }
        }

        // 2. Generate via AI
        var combinations = await aiService.BuildCombinationsAsync(matches, intent.UserMessage);

        // 3. Persist SYSTEM cache
        if (intent.SourceType == "SYSTEM" && combinations.Any())
        {
            var date = matches.FirstOrDefault()?.Date.Date ?? DateTimeOffset.UtcNow.Date;
            var existing = await dbContext.Combinations
                .FirstOrDefaultAsync(c => c.Date == date && 
                                         c.Language == "en" && 
                                         c.IsDailyCache);

            if (existing != null)
            {
                existing.Payload = System.Text.Json.JsonSerializer.Serialize(combinations, JsonOpts);
                existing.CreatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                dbContext.Combinations.Add(new Entities.Combination
                {
                    Date = date,
                    Language = "en",
                    IsDailyCache = true,
                    Payload = System.Text.Json.JsonSerializer.Serialize(combinations, JsonOpts),
                    CreatedAt = DateTimeOffset.UtcNow,
                    Name = $"Daily Portfolio {date:yyyy-MM-dd}"
                });
            }

            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        return combinations;
    }
}

