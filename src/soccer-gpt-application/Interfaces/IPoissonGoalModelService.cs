using System;

namespace soccer_gpt_application.Interfaces
{
    public interface IPoissonGoalModelService
    {
        PredictionResult PredictMatch(
            double homeAttackRating,
            double homeDefenseRating,
            double awayAttackRating,
            double awayDefenseRating,
            double leagueAvgGoals,
            double homeAdvantageFactor,
            double dixonColesRho);
    }

    public class PredictionResult
    {
        public double ExpectedGoalsHome { get; set; }
        public double ExpectedGoalsAway { get; set; }
        public double HomeWinProbability { get; set; }
        public double DrawProbability { get; set; }
        public double AwayWinProbability { get; set; }
        public double Over25Probability { get; set; }
        public double BttsProbability { get; set; }
        public double[,] ScoreMatrix { get; set; } = new double[0,0];
    }
}
