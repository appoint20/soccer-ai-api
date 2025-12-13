
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface IPredictionRepository
{
    Task<ApiFootballPrediction?> GetPredictionAsync(string leagueCode, string homeTeam, string awayTeam, string date, CancellationToken cancellationToken);
}
