
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface IHistoricalDataRepository
{
    Task<List<HistoricalMatchDto>> GetMatchesBetweenTeamsAsync(string teamA, string teamB, int lastN = 20);
}

public class HistoricalMatchDto
{
    public string Date { get; set; } = string.Empty;
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public int FTHG { get; set; } // Full Time Home Goals
    public int FTAG { get; set; } // Full Time Away Goals
    public string FTR { get; set; } = string.Empty; // Full Time Result (H, D, A)
}
