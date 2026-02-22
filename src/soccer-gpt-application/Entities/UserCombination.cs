using System.ComponentModel.DataAnnotations;

namespace soccer_gpt_application.Entities;

/// <summary>
/// Represents a combination/parlay manually created by the user to be tracked for backtesting
/// </summary>
public class UserCombination
{
    [Key]
    public int Id { get; init; }

    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // e.g. "Pending", "Won", "Lost"
    public string Status { get; set; } = "Pending";

    // Accumulated total odds for this parlay
    public double TotalOdds { get; set; }

    // Navigation property
    public List<UserCombinationMatch> Matches { get; set; } = new();
}

public class UserCombinationMatch
{
    [Key]
    public int Id { get; init; }
    
    public int UserCombinationId { get; set; }
    public UserCombination UserCombination { get; set; } = null!;

    public int FixtureId { get; set; }

    // e.g. "Both Teams To Score", "Match Winner"
    public string Market { get; set; } = string.Empty;

    // e.g. "Yes", "Home", "Over 2.5"
    public string Prediction { get; set; } = string.Empty;

    public double Odds { get; set; }
    public double Confidence { get; set; }
    
    // e.g. "Won", "Lost", "Pending"
    public string Status { get; set; } = "Pending";
}
