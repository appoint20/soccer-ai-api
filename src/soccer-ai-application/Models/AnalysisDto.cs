namespace SoccerAi.Application.Models;

public record AnalysisDto
{
    public DateTime Date { get; set; }
    public TimeSpan Time { get; set; }
    public string LeagueName { get; set; } = string.Empty;
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;

    public TeamAggregatedStats HomeLastFive { get; set; } = new();
    public TeamAggregatedStats HomeLastThreeAtHome { get; set; } = new();
    public TeamAggregatedStats AwayLastFive { get; set; } = new();
    public TeamAggregatedStats AwayLastThreeAtAway { get; set; } = new();

    public PoissonProbabilities Poisson { get; set; } = new();
    public MonteCarloResult MonteCarlo { get; set; } = new();
    public H2HStats HeadToHead { get; set; } = H2HStats.Insufficient;
    public MarketQualifications Qualifications { get; set; } = new();
    
    /// <summary>
    /// DecisionBuilder output - stricter qualification criteria
    /// </summary>
    public QualificationDecisions Decisions { get; set; } = new();
}