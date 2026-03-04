namespace SoccerAi.Application.Models;

public class PredictionResult
{
    public required MonteCarloResult MonteCarlo { get; set; }
    public double FinalBttsProbability { get; set; }
    public double FinalOver25Probability { get; set; }
}