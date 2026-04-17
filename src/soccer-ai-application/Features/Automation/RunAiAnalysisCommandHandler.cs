using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Features.Automation;

public class RunAiAnalysisCommandHandler(
    IAiSyncService aiSyncService,
    ILogger<RunAiAnalysisCommandHandler> logger)
    : ICommandHandler<RunAiAnalysisCommand>
{
    public async Task Handle(IReceiveContext<RunAiAnalysisCommand> context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Manual trigger processing via mediator for AI sync...");
        await aiSyncService.SyncUpcomingFixturesAsync(DateTime.UtcNow, false, cancellationToken);
    }
}
