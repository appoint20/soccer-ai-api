using SoccerAi.Application.Features.Combinations;

namespace SoccerAi.Application.Services.Combinations;

/// <summary>
/// One qualified pick as combination input.
/// </summary>
public sealed record BacktestPick(
    int FixtureId,
    string League,
    string HomeTeam,
    string AwayTeam,
    string Selection,
    double Probability,
    double Odds);

/// <summary>
/// Deterministic combination builder for the backtest (and any non-LLM flow).
///
/// Replaces the LLM-based IChatCombinationEngine in the backtest: combos_total
/// was 0 because ChatCombinationEngine delegates to IAiAnalysisService (an LLM
/// call) which returns nothing without an API key — and per global rules the
/// LLM must never drive the product metric anyway.
///
/// Builds 2-3 leg accumulators from qualified picks only, one leg per fixture,
/// ranked by average probability.
/// </summary>
public static class BacktestCombinationBuilder
{
    public const int MinLegs = 2;
    public const int MaxLegs = 3;
    public const int MaxCombinationsPerDay = 5;

    public static List<CombinationDto> Build(
        IReadOnlyList<BacktestPick> qualifiedPicks,
        double minSelectionOdds)
    {
        // One pick per fixture (the most probable one) and odds floor.
        var pool = qualifiedPicks
            .Where(p => p.Odds >= minSelectionOdds)
            .GroupBy(p => p.FixtureId)
            .Select(g => g.OrderByDescending(p => p.Probability).First())
            .OrderByDescending(p => p.Probability)
            .Take(12) // keep the combinatorics bounded
            .ToList();

        if (pool.Count < MinLegs) return [];

        var combos = new List<(List<BacktestPick> Legs, double AvgProb, double TotalOdds)>();
        for (var size = MinLegs; size <= MaxLegs; size++)
            Combine(pool, size, 0, [], combos);

        return combos
            .OrderByDescending(c => c.AvgProb)
            .Take(MaxCombinationsPerDay)
            .Select((c, i) => new CombinationDto
            {
                CombinationId = i + 1,
                Type = $"{c.Legs.Count}er",
                SourceType = "DETERMINISTIC",
                TotalOdds = Math.Round(c.TotalOdds, 2),
                Reason = $"Qualified picks only; avg probability {c.AvgProb:P0}.",
                Matches = c.Legs.Select(l => new CombinationMatchDto
                {
                    FixtureId = l.FixtureId,
                    League = l.League,
                    HomeTeam = l.HomeTeam,
                    AwayTeam = l.AwayTeam,
                    Selection = l.Selection,
                    Odds = l.Odds,
                    Confidence = l.Probability
                }).ToList()
            })
            .ToList();
    }

    private static void Combine(
        List<BacktestPick> pool, int size, int start, List<BacktestPick> current,
        List<(List<BacktestPick>, double, double)> results)
    {
        if (current.Count == size)
        {
            var avgProb = current.Average(p => p.Probability);
            var totalOdds = current.Aggregate(1.0, (acc, p) => acc * p.Odds);
            results.Add(([.. current], avgProb, totalOdds));
            return;
        }

        for (var i = start; i < pool.Count && results.Count < 100; i++)
        {
            current.Add(pool[i]);
            Combine(pool, size, i + 1, current, results);
            current.RemoveAt(current.Count - 1);
        }
    }
}
