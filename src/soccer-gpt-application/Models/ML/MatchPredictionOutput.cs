
namespace soccer_gpt_application.Models.ML;

public class MatchPredictionOutput
{
    public float ExpectedGoals { get; set; }
    public float Over15Probability { get; set; }
    public float Over25Probability { get; set; }
    
    public float DrawProbability { get; set; }
    public float ZeroZeroProbability { get; set; }
    
    // The "Safety Layer" output
    public float LowGoalTrapProbability { get; set; }

    public float BTTSProbability { get; set; } // Added
    public float HomeWinProbability { get; set; }
    public float AwayWinProbability { get; set; }

    // Final Scores
    public float Over15Score { get; set; }
    public float Over25Score { get; set; }
    public float BTTSScore { get; set; } // Added
    public float FinalOverGoalsConfidence { get; set; }
    
    public List<string> Reasons { get; set; } = new();
}
