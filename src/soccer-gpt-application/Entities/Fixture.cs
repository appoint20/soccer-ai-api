using System.ComponentModel.DataAnnotations;

namespace soccer_gpt_application.Entities;

public class Fixture
{
    public int Id { get; init; }
    
    public DateTime Date { get; init; }
    public TimeSpan Time { get; init; }
    
    [MaxLength(100)]
    public string HomeName { get; init; } = string.Empty;
    
    [MaxLength(100)]
    public string AwayName { get; init; } = string.Empty;
    
    [MaxLength(100)]
    public string LeagueName { get; init; } = string.Empty;

    [MaxLength(255)]
    public string Signature { get; set; } = string.Empty;
    
    public double? HomeOdds { get; init; }
    public double? AwayOdds { get; init; }
    public double? DrawOdds { get; init; }
    
    public double? Over25Odds { get; init; }
    public double? Under25Odds { get; init; }
    public double? BttsOdds { get; init; }
    public double? TwoToThreeGoalsOdds { get; init; }
    
    public bool Played { get; init; }
}
