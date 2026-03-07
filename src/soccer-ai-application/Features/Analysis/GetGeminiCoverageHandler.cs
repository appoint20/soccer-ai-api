using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Application.Features.Analysis;

public class GetGeminiCoverageHandler(IApplicationDbContext dbContext) 
    : IRequestHandler<GetGeminiCoverageQuery, GetGeminiCoverageResponse>
{
    public async Task<GetGeminiCoverageResponse> Handle(IReceiveContext<GetGeminiCoverageQuery> context, CancellationToken cancellationToken)
    {
        var daysAhead = context.Message.DaysAhead;
        var today = DateTime.UtcNow.Date;
        
        // Entity Framework handles the UTC ticks SQLite conversion inside OnModelCreating automatically 
        var startOffset = new DateTimeOffset(today, TimeSpan.Zero);
        var endOffset = new DateTimeOffset(today.AddDays(daysAhead), TimeSpan.Zero);

        // Query fixtures and determine if they possess English Gemini analyses
        var fixtures = await dbContext.Fixtures
            .Where(f => f.Date >= startOffset && f.Date < endOffset)
            .Select(f => new 
            {
                f.Date,
                HasAnalysis = dbContext.FixtureAnalyses.Any(a => a.FixtureId == f.Id && a.Lang == "en")
            })
            .ToListAsync(cancellationToken);

        // Group explicitly by Date string and count metrics
        var coverage = fixtures
            .GroupBy(f => f.Date.ToString("yyyy-MM-dd"))
            .Select(g => new GeminiCoverageDto(
                Date: g.Key,
                TotalMatches: g.Count(),
                AnalyzedMatches: g.Count(x => x.HasAnalysis),
                PendingMatches: g.Count(x => !x.HasAnalysis)
            ))
            .OrderBy(x => x.Date)
            .ToList();

        return new GetGeminiCoverageResponse { Coverage = coverage };
    }
}
