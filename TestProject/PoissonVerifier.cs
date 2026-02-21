using System;
using soccer_gpt_infrastructure.Services;

namespace TestProject
{
    public static class PoissonVerifier
    {
        public static void Run()
        {
            Console.WriteLine("========================================");
            Console.WriteLine("  C# GOAL PREDICTION MODEL VERIFICATION ");
            Console.WriteLine("  Poisson with Dixon-Coles Correction   ");
            Console.WriteLine("========================================\n");

            // Dummy Data
            double homeAttack = 1.45;
            double homeDefense = 0.80;
            double awayAttack = 0.75;
            double awayDefense = 1.30;
            double leagueAvg = 1.35;
            double homeAdv = 1.12;
            double rho = -0.13;

            Console.WriteLine("INPUT PARAMETERS:");
            Console.WriteLine($"  Home Attack         : {homeAttack}");
            Console.WriteLine($"  Home Defense        : {homeDefense}");
            Console.WriteLine($"  Away Attack         : {awayAttack}");
            Console.WriteLine($"  Away Defense        : {awayDefense}");
            Console.WriteLine($"  League Avg Goals    : {leagueAvg}");
            Console.WriteLine($"  Home Advantage      : {homeAdv}");
            Console.WriteLine($"  Rho                 : {rho}");
            Console.WriteLine("----------------------------------------\n");

            var service = new PoissonGoalModelService();
            var result = service.PredictMatch(
                homeAttack, homeDefense, 
                awayAttack, awayDefense, 
                leagueAvg, homeAdv, rho);

            Console.WriteLine("PREDICTION RESULTS:");
            Console.WriteLine($"  Expected Goals (Home) : {result.ExpectedGoalsHome:F4}");
            Console.WriteLine($"  Expected Goals (Away) : {result.ExpectedGoalsAway:F4}");
            Console.WriteLine("-" + new string('-', 30));
            Console.WriteLine($"  Home Win Prob         : {result.HomeWinProbability:P2}");
            Console.WriteLine($"  Draw Prob             : {result.DrawProbability:P2}");
            Console.WriteLine($"  Away Win Prob         : {result.AwayWinProbability:P2}");
            Console.WriteLine("-" + new string('-', 30));
            Console.WriteLine($"  Over 2.5 Goals Prob   : {result.Over25Probability:P2}");
            Console.WriteLine($"  BTTS Probability      : {result.BttsProbability:P2}");

            Console.WriteLine("\nSCORE MATRIX (Top-Left 4x4):");
            Console.WriteLine("      A=0     A=1     A=2     A=3");
            for (int h = 0; h < 4; h++)
            {
                Console.Write($"H={h} |");
                for (int a = 0; a < 4; a++)
                {
                    double val = result.ScoreMatrix[h, a];
                    Console.Write($" {val:P1}  ");
                }
                Console.WriteLine();
            }
        }
    }
}
