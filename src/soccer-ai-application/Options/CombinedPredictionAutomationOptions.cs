namespace SoccerAi.Application.Options;

public sealed class CombinedPredictionAutomationOptions
{
    public const string SectionName = "CombinedPredictionAutomation";
    public bool Enabled { get; set; }
    public int PollIntervalMinutes { get; set; } = 120;
    public double HorizonHours { get; set; } = 24;
    public double FinalWindowHours { get; set; } = 4;
    public int MinimumLeadMinutes { get; set; } = 30;
    public int MaxRefreshesPerRun { get; set; } = 3;
    public int MaxRefreshesPerDay { get; set; } = 5;
    public int RefreshTimeoutMinutes { get; set; } = 20;
    public bool RequireAcceptedMl { get; set; } = true;
}
