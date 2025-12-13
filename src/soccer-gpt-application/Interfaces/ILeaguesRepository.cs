
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface ILeaguesRepository
{
    Task<List<LeagueDto>> GetLeaguesAsync(CancellationToken cancellationToken);
}
