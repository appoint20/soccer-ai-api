namespace soccer_gpt_application.Models;

public class PredictionResult
{
    public MonteCarloResult MonteCarlo { get; set; }
    public double FinalBttsProbability { get; set; }
    public double FinalOver25Probability { get; set; }
}