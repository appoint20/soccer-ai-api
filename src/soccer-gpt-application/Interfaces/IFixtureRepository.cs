
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface IFixtureRepository
{
    Task<List<UpcomingMatchDto>> GetFixturesAsync(int offset, int limit, CancellationToken cancellationToken);
    Task<int> GetTotalCountAsync(CancellationToken cancellationToken);
}
