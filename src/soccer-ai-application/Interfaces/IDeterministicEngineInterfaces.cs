using SoccerAi.Application.Models.Deterministic;

namespace SoccerAi.Application.Interfaces;

public interface INlpService
{
    Task<NlpIntent> ParseIntentAsync(string query);
}

public interface IMatchRepository
{
    Task<List<Match>> GetUpcomingMatchesAsync(System.DateTimeOffset? date = null);
}

public interface ICombinationService
{
    List<Combination> GenerateCombinations(List<Match> matches, NlpIntent intent);
}
