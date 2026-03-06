using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Features.Automation;

public class RunGeminiAnalysisCommandHandler(
    IGeminiSyncService geminiSyncService,
    IMatchAnalysisService analysisService,
    ILogger<RunGeminiAnalysisCommandHandler> logger)
    : ICommandHandler<RunGeminiAnalysisCommand>
{
    public async Task Handle(IReceiveContext<RunGeminiAnalysisCommand> context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Manual trigger processing via mediator for Gemini sync...");
        await geminiSyncService.SyncUpcomingFixturesAsync(DateTime.UtcNow, cancellationToken);
    }
}
