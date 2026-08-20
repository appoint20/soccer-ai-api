using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Exceptions;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Application.Features.Auth;

/// <summary>
/// Hard-deletes the authenticated user's account after confirming their
/// password. Required by Google Play and Apple guideline 5.1.1(v).
/// </summary>
public sealed record DeleteAccountCommand(int UserId, string Password) : ICommand;

public sealed record DeleteAccountResponse(bool Deleted) : IResponse;

public sealed class DeleteAccountHandler(
    IApplicationDbContext dbContext,
    ILogger<DeleteAccountHandler> logger)
    : ICommandHandler<DeleteAccountCommand, DeleteAccountResponse>
{
    public async Task<DeleteAccountResponse> Handle(
        IReceiveContext<DeleteAccountCommand> context,
        CancellationToken cancellationToken)
    {
        var command = context.Message;

        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == command.UserId, cancellationToken);

        if (user is null)
        {
            logger.LogWarning("Delete-account requested for non-existent user {UserId}", command.UserId);
            throw new NotFoundException($"User {command.UserId} not found.");
        }

        if (!BCrypt.Net.BCrypt.Verify(command.Password, user.PasswordHash))
        {
            logger.LogWarning("Delete-account wrong password for user {UserId}", command.UserId);
            throw new ForbiddenException("Incorrect password.");
        }

        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} ({Username}) deleted their account", user.Id, user.Username);

        return new DeleteAccountResponse(Deleted: true);
    }
}
