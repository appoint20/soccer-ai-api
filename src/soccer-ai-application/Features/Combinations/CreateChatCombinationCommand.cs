using Mediator.Net.Contracts;
using SoccerAi.Application.Features.Combinations;

namespace SoccerAi.Application.Features.Combinations;

public class CreateChatCombinationCommand : ICommand
{
    public string Query { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
}

public class CreateChatCombinationResponse : IResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<CombinationDto> Combinations { get; set; } = new();
    public string AiReasoning { get; set; } = string.Empty;
}
