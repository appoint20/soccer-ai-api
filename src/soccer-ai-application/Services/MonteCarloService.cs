using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Services;

public class MonteCarloService(int? seed = null): IMonteCarloService
{
    private readonly Random _random = seed.HasValue ? new Random(seed.Value) : new Random();
    public PredictionResult Predict(PoissonProbabilities poissonProbabilities, MarketOdds? market = null)
    {
        // STEP 1 — Run Monte Carlo using λ
        var mcResult = Simulate(
            poissonProbabilities.HomeExpectedGoals,
            poissonProbabilities.AwayExpectedGoals,
            simulations: 50000
        );

        // STEP 2 — Market calibration
        var finalBtts = mcResult.BttsProbability;
        var finalOver25 = mcResult.Over25Probability;

        if (market == null)
            return new PredictionResult
            {
                MonteCarlo = mcResult,
                FinalBttsProbability = finalBtts,
                FinalOver25Probability = finalOver25
            };
        
        finalBtts = MarketCalibrationService.BayesianUpdate(
            finalBtts,
            market.BttsOdds.OddsToProbability()
        );

        finalOver25 = MarketCalibrationService.BayesianUpdate(
            finalOver25,
            market.Over25Odds.OddsToProbability()
        );

        // STEP 4 — Final result
        return new PredictionResult
        {
            MonteCarlo = mcResult,
            FinalBttsProbability = finalBtts,
            FinalOver25Probability = finalOver25
        };
    }


    private MonteCarloResult Simulate(
        double lambdaHome,
        double lambdaAway,
        int simulations = 50000,
        int maxGoals = 7)
    {
        int homeWin = 0, draw = 0, awayWin = 0;
        int bttsYes = 0, over25 = 0, goals23 = 0;

        int zeroZero = 0, oneZero = 0, zeroOne = 0;

        double totalHomeGoals = 0;
        double totalAwayGoals = 0;

        var scoreCounter = new Dictionary<string, int>();

        for (var i = 0; i < simulations; i++)
        {
            int homeGoals = SamplePoisson(lambdaHome);
            int awayGoals = SamplePoisson(lambdaAway);

            totalHomeGoals += homeGoals;
            totalAwayGoals += awayGoals;

            int total = homeGoals + awayGoals;

            // Match result
            if (homeGoals > awayGoals) homeWin++;
            else if (homeGoals < awayGoals) awayWin++;
            else draw++;

            // BTTS
            if (homeGoals > 0 && awayGoals > 0)
                bttsYes++;

            // Over 2.5
            if (total >= 3)
                over25++;

            // 2-3 goals
            if (total == 2 || total == 3)
                goals23++;

            // Low score detection
            if (homeGoals == 0 && awayGoals == 0) zeroZero++;
            if (homeGoals == 1 && awayGoals == 0) oneZero++;
            if (homeGoals == 0 && awayGoals == 1) zeroOne++;

            // Score distribution
            if (homeGoals <= maxGoals && awayGoals <= maxGoals)
            {
                var key = $"{homeGoals}-{awayGoals}";
                scoreCounter.TryAdd(key, 0);
                scoreCounter[key]++;
            }
        }

        // Normalize score matrix
        var scoreMatrix = scoreCounter.ToDictionary(
            x => x.Key,
            x => (double)x.Value / simulations);

        return new MonteCarloResult
        {
            BttsProbability = (double)bttsYes / simulations,
            Over25Probability = (double)over25 / simulations,
            Under25Probability = 1 - ((double)over25 / simulations),

            HomeWinProbability = (double)homeWin / simulations,
            DrawProbability = (double)draw / simulations,
            AwayWinProbability = (double)awayWin / simulations,

            ZeroZeroProbability = (double)zeroZero / simulations,
            OneZeroProbability = (double)oneZero / simulations,
            ZeroOneProbability = (double)zeroOne / simulations,

            TwoToThreeGoalsProbability = (double)goals23 / simulations,

            ExpectedHomeGoals = totalHomeGoals / simulations,
            ExpectedAwayGoals = totalAwayGoals / simulations,

            ScoreMatrix = scoreMatrix
        };
    }

    // Knuth Poisson sampler (fast + stable)
    private int SamplePoisson(double lambda)
    {
        double l = Math.Exp(-lambda);
        int k = 0;
        double p = 1;

        do
        {
            k++;
            p *= _random.NextDouble();
        }
        while (p > l);

        return k - 1;
    }
}