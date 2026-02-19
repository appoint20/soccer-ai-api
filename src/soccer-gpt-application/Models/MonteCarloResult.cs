namespace soccer_gpt_application.Models;

public sealed class MonteCarloResult
{
    public double BttsProbability { get; set; }
    public double Over25Probability { get; set; }
    public double Under25Probability { get; set; }

    public double HomeWinProbability { get; set; }
    public double DrawProbability { get; set; }
    public double AwayWinProbability { get; set; }

    public double ZeroZeroProbability { get; set; }
    public double OneZeroProbability { get; set; }
    public double ZeroOneProbability { get; set; }

    public double TwoToThreeGoalsProbability { get; set; }

    public double ExpectedHomeGoals { get; set; }
    public double ExpectedAwayGoals { get; set; }

    public Dictionary<string, double> ScoreMatrix { get; set; } = new();
}
