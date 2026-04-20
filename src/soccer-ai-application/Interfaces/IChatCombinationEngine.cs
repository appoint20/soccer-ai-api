using SoccerAi.Application.Models;
using SoccerAi.Application.Features.Combinations;

namespace SoccerAi.Application.Interfaces;

public interface IChatCombinationEngine
{
    Task<List<CombinationDto>> GenerateCombinationsAsync(List<MatchAnalysis> matches, ChatCombinationIntent intent);
}
