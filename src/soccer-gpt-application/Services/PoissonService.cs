using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Services;

public sealed class PoissonService : IPoissonService
{
    private const int MinimumSampleSize = 3;

    public StrengthFactors Build(
        TeamAggregatedStats homeTeamHomeStats, TeamAggregatedStats awayTeamAwayStats, LeagueGoalAverages leagueAverages)
    {
        if (!IsValidInput(homeTeamHomeStats, awayTeamAwayStats, leagueAverages))
            throw new Exception("Poisson calculation not possible");

        var strengths = CalculateStrengths(homeTeamHomeStats, awayTeamAwayStats, leagueAverages);
        var expectedGoals = CalculateExpectedGoals(strengths, leagueAverages);

        return new StrengthFactors
        {
            HomeAttackStrength = strengths.HomeAttackStrength,
            HomeDefenseStrength = strengths.HomeDefenseStrength,
            AwayAttackStrength = strengths.AwayAttackStrength,
            AwayDefenseStrength = strengths.AwayDefenseStrength,
            HomeExpectedGoals = expectedGoals.Home,
            AwayExpectedGoals = expectedGoals.Away
        };
    }
    
    public PoissonProbabilities CalculateProbabilities(StrengthFactors poissonAnalysis)
    {
        if (!poissonAnalysis.IsValid)
            return new PoissonProbabilities();

        var homeExpectedGoals = poissonAnalysis.HomeExpectedGoals;
        var awayExpectedGoals = poissonAnalysis.AwayExpectedGoals;
        var matrix = BuildScoreMatrix(homeExpectedGoals, awayExpectedGoals);
        
        return new PoissonProbabilities
        {
            HomeExpectedGoals = Math.Round(homeExpectedGoals, 2),
            AwayExpectedGoals = Math.Round(awayExpectedGoals, 2),
            
            HomeWin = Round(CalculateHomeWin(matrix)),
            Draw = Round(CalculateDraw(matrix)),
            AwayWin = Round(CalculateAwayWin(matrix)),
            
            Over25 = Round(CalculateOver25(matrix)),
            Under25 = Round(1 - CalculateOver25(matrix)),
            
            BTTS = Round(CalculateBtts(matrix)),
            BTTSNo = Round(1 - CalculateBtts(matrix)),
            
            TwoToThreeGoals = Round(CalculateTwoToThreeGoals(matrix)),
            
            MostLikelyScore = GetMostLikelyScore(matrix).Score,
            MostLikelyScoreProbability = Round(GetMostLikelyScore(matrix).Probability),
            
            TopScores = GetTopScores(matrix, 5)
        };
    }
    
    

    private static double[,] BuildScoreMatrix(double lambdaHome, double lambdaAway, int maxGoals = 7)
    {
        var matrix = new double[maxGoals, maxGoals];
        
        for (var h = 0; h < maxGoals; h++)
        {
            for (var a = 0; a < maxGoals; a++)
            {
                matrix[h, a] = Poisson(lambdaHome, h) * Poisson(lambdaAway, a);
            }
        }
        
        return matrix;
    }

    
    private static double Round(double value) => Math.Round(value, 3);
    
    private static double Poisson(double lambda, int k) 
        => Math.Pow(lambda, k) * Math.Exp(-lambda) / Factorial(k);
    
    private static double Factorial(int n)
    {
        if (n <= 1) return 1;
        double result = 1;
        for (var i = 2; i <= n; i++)
            result *= i;
        return result;
    }

    private static bool IsValidInput(
        TeamAggregatedStats home,
        TeamAggregatedStats away,
        LeagueGoalAverages league)
    {
        if (home.MatchesPlayed < MinimumSampleSize)
            return false;
        
        return away.MatchesPlayed >= MinimumSampleSize && league.IsValid;
    }

    private static StrengthFactors CalculateStrengths(
        TeamAggregatedStats home,
        TeamAggregatedStats away,
        LeagueGoalAverages league)
    {
        var homeAttack = SafeDivide(home.GoalsScoredAvg, league.HomeGoalsAvg);
        var homeDefense = SafeDivide(home.GoalsConcededAvg, league.AwayGoalsAvg);

        var awayAttack = SafeDivide(away.GoalsScoredAvg, league.AwayGoalsAvg);
        var awayDefense = SafeDivide(away.GoalsConcededAvg, league.HomeGoalsAvg);

        return new StrengthFactors
        {
            HomeAttackStrength = Clamp(homeAttack, 0.5, 2.5),
            HomeDefenseStrength = Clamp(homeDefense, 0.5, 2.5),
            AwayAttackStrength = Clamp(awayAttack, 0.5, 2.5),
            AwayDefenseStrength = Clamp(awayDefense, 0.5, 2.5)
        };
    }

    private static (double Home, double Away) CalculateExpectedGoals(
        StrengthFactors s,
        LeagueGoalAverages league)
    {
        var lambdaHome = league.HomeGoalsAvg * s.HomeAttackStrength * s.AwayDefenseStrength;
        var lambdaAway = league.AwayGoalsAvg * s.AwayAttackStrength * s.HomeDefenseStrength;

        lambdaHome = Clamp(lambdaHome, 0.3, 4.5);
        lambdaAway = Clamp(lambdaAway, 0.3, 4.5);

        return (Math.Round(lambdaHome, 3), Math.Round(lambdaAway, 3));
    }

    private static double SafeDivide(double numerator, double denominator) 
        => denominator == 0 ? 1.0 : numerator / denominator;

    private static double Clamp(double value, double min, double max)
        => Math.Max(min, Math.Min(max, value));
    


    private static double CalculateHomeWin(double[,] matrix)
    {
        double prob = 0;
        int size = matrix.GetLength(0);
        
        for (int h = 0; h < size; h++)
            for (int a = 0; a < h; a++)
                prob += matrix[h, a];
        
        return prob;
    }

    private static double CalculateDraw(double[,] matrix)
    {
        double prob = 0;
        int size = matrix.GetLength(0);
        
        for (int i = 0; i < size; i++)
            prob += matrix[i, i];
        
        return prob;
    }

    private static double CalculateAwayWin(double[,] matrix)
    {
        double prob = 0;
        int size = matrix.GetLength(0);
        
        for (int h = 0; h < size; h++)
            for (int a = h + 1; a < size; a++)
                prob += matrix[h, a];
        
        return prob;
    }

    private static double CalculateOver25(double[,] matrix)
    {
        double under25 = 0;
        int size = matrix.GetLength(0);
        
        // Under 2.5 = 0-0, 1-0, 0-1, 1-1, 2-0, 0-2
        for (int h = 0; h < size; h++)
        {
            for (int a = 0; a < size; a++)
            {
                if (h + a < 3)
                    under25 += matrix[h, a];
            }
        }
        
        return 1 - under25;
    }

    private static double CalculateBtts(double[,] matrix)
    {
        double btts = 0;
        var size = matrix.GetLength(0);
        
        for (var h = 1; h < size; h++)
            for (var a = 1; a < size; a++)
                btts += matrix[h, a];
        
        return btts;
    }

    private static double CalculateTwoToThreeGoals(double[,] matrix)
    {
        double prob = 0;
        var size = matrix.GetLength(0);
        
        for (var h = 0; h < size; h++)
        {
            for (var a = 0; a < size; a++)
            {
                var total = h + a;
                if (total is 2 or 3)
                    prob += matrix[h, a];
            }
        }
        
        return prob;
    }

    private static ScoreProbability GetMostLikelyScore(double[,] matrix)
    {
        int bestH = 0, bestA = 0;
        double bestProb = 0;
        var size = matrix.GetLength(0);
        
        for (var h = 0; h < size; h++)
        {
            for (var a = 0; a < size; a++)
            {
                if (!(matrix[h, a] > bestProb))
                    continue;
                
                bestProb = matrix[h, a];
                bestH = h;
                bestA = a;
            }
        }
        
        return new ScoreProbability
        {
            HomeGoals = bestH,
            AwayGoals = bestA,
            Probability = bestProb
        };
    }

    private static List<ScoreProbability> GetTopScores(double[,] matrix, int count)
    {
        var scores = new List<ScoreProbability>();
        var size = matrix.GetLength(0);
        
        for (var h = 0; h < size; h++)
        {
            for (var a = 0; a < size; a++)
            {
                scores.Add(new ScoreProbability
                {
                    HomeGoals = h,
                    AwayGoals = a,
                    Probability = Round(matrix[h, a])
                });
            }
        }
        
        return scores
            .OrderByDescending(s => s.Probability)
            .Take(count)
            .ToList();
    }
}
