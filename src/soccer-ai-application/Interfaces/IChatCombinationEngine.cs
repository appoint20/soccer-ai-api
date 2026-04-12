using SoccerAi.Application.Models;
using SoccerAi.Application.Features.Combinations;

namespace SoccerAi.Application.Interfaces;

public interface IChatCombinationEngine
{
    List<CombinationDto> GenerateCombinations(List<MatchAnalysis> matches, ChatCombinationIntent intent);
}
