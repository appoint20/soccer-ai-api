public class SyncResult
{
    public int LeaguesSynced { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public List<string> ErrorMessages { get; set; } = new();
    
    // For legacy compatibility where only count was used
    public int Errors => ErrorMessages.Count;
}
