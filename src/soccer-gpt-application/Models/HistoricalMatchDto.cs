using System.Text.Json.Serialization;

namespace soccer_gpt_application.Models;

public record HistoricalMatchDto
{
    public DateTime Date { get; init; }
    public string Time { get; init; } = string.Empty;
    public string Div { get; init; } = string.Empty; // League Code
    public string League => Div; // Alias for compatibility
    
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    
    public string Referee { get; init; } = string.Empty;

    public int FTHG { get; init; }
    public int FTAG { get; init; }
    public string FTR { get; init; } = string.Empty;

    // Odds (B365)
    public decimal B365H { get; init; }
    public decimal B365D { get; init; }
    public decimal B365A { get; init; }
    public decimal B365Over25 { get; init; }
    public decimal B365Under25 { get; init; }
    
    // Helper Properties
    public bool IsOver25 => (FTHG + FTAG) > 2.5;
    public bool IsBtts => FTHG > 0 && FTAG > 0;
    public bool Is2to3Goals => (FTHG + FTAG) >= 2 && (FTHG + FTAG) <= 3;
}
