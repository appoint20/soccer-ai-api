using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface IHistoricalDataRepository
{
    // Used to be async, but now it's in-memory. 
    // We can keep Task signature to avoid breaking too many callers, or switch to sync.
    // Keeping Task for now to minimize ripple effect, will just return Task.FromResult.
    Task<List<HistoricalMatchDto>> GetMatchesBetweenTeamsAsync(string teamA, string teamB, int lastN = 20);
    Task<List<HistoricalMatchDto>> GetAllMatchesAsync();
    
    // New methods for analytics
    List<HistoricalMatchDto> GetMatchesForTeam(string teamName); // Sync access is fine too
}
