using Mediator.Net.Context;
using Mediator.Net.Contracts;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Application.Features.Combinations;

public class CreateUserCombinationCommand : ICommand
{
    public string Name { get; set; } = string.Empty;
    public List<CreateUserCombinationMatchDto> Matches { get; set; } = new();
}

public class CreateUserCombinationMatchDto
{
    public int FixtureId { get; set; }
    public string Market { get; set; } = string.Empty;
    public string Prediction { get; set; } = string.Empty;
    public double Odds { get; set; }
    public double Confidence { get; set; }
}

public class CreateUserCombinationResponse : IResponse
{
    public int CombinationId { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class CreateUserCombinationCommandHandler(IApplicationDbContext dbContext) 
    : ICommandHandler<CreateUserCombinationCommand, CreateUserCombinationResponse>
{
    public async Task<CreateUserCombinationResponse> Handle(IReceiveContext<CreateUserCombinationCommand> context, CancellationToken cancellationToken)
    {
        var cmd = context.Message;

        if (cmd.Matches.Count == 0)
        {
            return new CreateUserCombinationResponse { Success = false, Message = "No matches provided." };
        }

        var totalOdds = 1.0;
        foreach (var m in cmd.Matches)
        {
            if (m.Odds > 0) totalOdds *= m.Odds;
        }

        var userCombination = new Combination
        {
            Name = string.IsNullOrWhiteSpace(cmd.Name) ? $"Custom Combo {DateTime.UtcNow:yyyy-MM-dd}" : cmd.Name,
            CreatedAt = DateTime.UtcNow,
            Status = "Pending",
            TotalOdds = Math.Round(totalOdds, 2)
        };

        dbContext.Combinations.Add(userCombination);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateUserCombinationResponse 
        { 
            Success = true, 
            CombinationId = userCombination.Id,
            Message = "Combination saved successfully."
        };
    }
}
