namespace soccer_gpt_application.Models;

public class MigrationResult
{
    public int MatchesProcessed { get; set; }
    public int MatchesAdded { get; set; }
    public int MatchesSkipped { get; set; }
    public List<string> Errors { get; init; } = [];
}
