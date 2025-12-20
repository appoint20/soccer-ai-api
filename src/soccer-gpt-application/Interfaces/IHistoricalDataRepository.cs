
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface IHistoricalDataRepository
{
    Task<List<HistoricalMatchDto>> GetMatchesBetweenTeamsAsync(string teamA, string teamB, int lastN = 20);
    Task<List<HistoricalMatchDto>> GetAllMatchesAsync();
}

public class HistoricalMatchDto
{
    public DateTime Date { get; set; }
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty; // Added
    public string Season { get; set; } = string.Empty; // Added
    public string Referee { get; set; } = string.Empty; // Added
    public int FTHG { get; set; } // Full Time Home Goals
    public int FTAG { get; set; } // Full Time Away Goals
    public int HTHG { get; set; } // Half Time Home Goals
    public int HTAG { get; set; } // Half Time Away Goals
    public int HS { get; set; }   // Home Shots
    public int AS { get; set; }   // Away Shots
    public int HST { get; set; }  // Home Shots Target
    public int AST { get; set; }  // Away Shots Target
    public int HC { get; set; }   // Home Corners
    public int AC { get; set; }   // Away Corners
    
    public string FTR { get; set; } = string.Empty; // Full Time Result (H, D, A)
    public MatchOddsDto? Odds { get; set; }
}

public class MatchOddsDto
{
    public decimal HomeWin { get; set; }
    public decimal Draw { get; set; }
    public decimal AwayWin { get; set; }
    public decimal Over25 { get; set; }
    public decimal Under25 { get; set; }
    public decimal BttsYes { get; set; }
}
