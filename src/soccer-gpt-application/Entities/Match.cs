using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace soccer_gpt_application.Entities;

public class Match
{
    [Key]
    public int Id { get; init; }

    public DateTime Date { get; init; }
    
    public TimeSpan Time { get; init; }
    
    [MaxLength(20)]
    public string LeagueName { get; init; } = string.Empty;

    // Foreign Keys to Team
    public int HomeTeamId { get; init; }
    [ForeignKey("HomeTeamId")]
    public virtual Team HomeTeam { get; init; } = null!;

    public int AwayTeamId { get; init; }
    [ForeignKey("AwayTeamId")]
    public virtual Team AwayTeam { get; init; } = null!;

    public int FullTimeHomeGoal { get; init; }
    public int FullTimeAwayGoal { get; init; }

    [MaxLength(5)]
    public string FullTimeResult { get; init; } = string.Empty;

    public int HalfTimeHomeGoal { get; init; }
    public int HalfTimeAwayGoal { get; init; }

    [MaxLength(5)]
    public string HalfTimeResult { get; init; } = string.Empty;

    public bool CurrentSeason { get; init; }

    [MaxLength(100)]
    public string Referee { get; init; } = string.Empty;
}
