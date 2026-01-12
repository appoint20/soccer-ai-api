using System.ComponentModel.DataAnnotations;

namespace soccer_gpt_application.Entities;

public class Team
{
    [Key]
    public int Id { get; init; }

    [Required]
    [MaxLength(100)]
    public string Name { get; init; } = string.Empty;
    
    // Reverse navigation if needed, or keeping it clean
    public virtual ICollection<Match> HomeMatches { get; init; } = new List<Match>();
    public virtual ICollection<Match> AwayMatches { get; init; } = new List<Match>();
}
