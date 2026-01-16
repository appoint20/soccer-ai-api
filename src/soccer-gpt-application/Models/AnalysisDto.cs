namespace soccer_gpt_application.Models;

public record AnalysisDto
{
    public DateTime Date { get; set; }
    public TimeSpan Time { get; set; }
    public string LeagueName { get; set; } = string.Empty;
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;

    public TeamAggregatedStats HomeLastSeven { get; set; } = new();
    public TeamAggregatedStats HomeLastThreeAtHome { get; set; } = new();
    public TeamAggregatedStats AwayLastSeven { get; set; } = new();
    public TeamAggregatedStats AwayLastThreeAtAway { get; set; } = new();

    public PoissonProbabilities AdvancedAnalytics { get; set; } = new();
}