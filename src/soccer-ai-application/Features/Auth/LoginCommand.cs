using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.EntityFrameworkCore;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using BCrypt.Net;

namespace SoccerAi.Application.Features.Auth;

public record LoginCommand(string Username, string Password) : ICommand;

public record LoginResponse(string Token) : IResponse;

public class LoginHandler(IApplicationDbContext dbContext, IJwtService jwtService) : ICommandHandler<LoginCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(IReceiveContext<LoginCommand> context, CancellationToken cancellationToken)
    {
        var request = context.Message;
        
        // Seed users if none exist (per request)
        if (!await dbContext.Users.AnyAsync(cancellationToken))
        {
            await SeedUsersAsync(cancellationToken);
        }

        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username, cancellationToken);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid username or password");
        }

        var token = jwtService.GenerateToken(user.Id, user.Username);
        return new LoginResponse(token);
    }

    private async Task SeedUsersAsync(CancellationToken cancellationToken)
    {
        if (!await dbContext.Users.AnyAsync(u => u.Username == "wk", cancellationToken))
            dbContext.Users.Add(new User { Username = "wk", PasswordHash = BCrypt.Net.BCrypt.HashPassword("masterlockunlocked") });

        if (!await dbContext.Users.AnyAsync(u => u.Username == "rajev", cancellationToken))
            dbContext.Users.Add(new User { Username = "rajev", PasswordHash = BCrypt.Net.BCrypt.HashPassword("masterlockunderdog") });

        if (!await dbContext.Users.AnyAsync(u => u.Username == "sahil", cancellationToken))
            dbContext.Users.Add(new User { Username = "sahil", PasswordHash = BCrypt.Net.BCrypt.HashPassword("mastermind") });

        if (!await dbContext.Users.AnyAsync(u => u.Username == "shivm", cancellationToken))
            dbContext.Users.Add(new User { Username = "shivm", PasswordHash = BCrypt.Net.BCrypt.HashPassword("destroyer") });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
