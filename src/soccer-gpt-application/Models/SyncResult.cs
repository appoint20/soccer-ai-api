namespace soccer_gpt_application.Models;

public class SyncResult
{
    public int LeaguesSynced { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Errors { get; set; }
}
