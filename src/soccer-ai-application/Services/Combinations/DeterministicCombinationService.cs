using System;
using System.Collections.Generic;
using System.Linq;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models.Deterministic;

namespace SoccerAi.Application.Services.Combinations;

public class DeterministicCombinationService : ICombinationService
{
    private const double ProbabilityWeight = 0.6;
    private const double FormWeight = 0.3;
    private const double OddsWeight = 0.1;

    public List<Combination> GenerateCombinations(List<Match> matches, NlpIntent intent)
    {
        // Step 1: Filter and Map candidates
        var selections = FilterMatches(matches, intent);
        
        // Step 2: Generate combinations of size N (2-3)
        var combinations = new List<Combination>();
        foreach (var size in intent.NumMatches)
        {
            if (size < 2 || size > 3) continue;
            var generated = GenerateRecursive(selections, size);
            combinations.AddRange(generated);
        }

        // Step 3: Score and Rank
        foreach (var combo in combinations)
        {
            combo.Score = CalculateScore(combo);
            combo.Reasoning = GenerateReasoning(combo);
        }

        return combinations
            .OrderByDescending(c => c.Score)
            .Take(5)
            .ToList();
    }

    private List<MatchSelection> FilterMatches(List<Match> matches, NlpIntent intent)
    {
        var result = new List<MatchSelection>();
        var minProb = intent.Filters.MinProbability > 0 ? intent.Filters.MinProbability : 0.6;
        var allowedLeagues = intent.Filters.Leagues.Select(l => l.ToLowerInvariant()).ToList();

        foreach (var match in matches)
        {
            // League filter
            if (allowedLeagues.Any() && !allowedLeagues.Any(l => match.League.ToLowerInvariant().Contains(l)))
                continue;

            // Bet Type logic (default to Win as per Step 3)
            if (intent.BetType.ToLower() == "win")
            {
                if (match.HomeWinProbability >= minProb)
                {
                    result.Add(new MatchSelection { Match = match, BetType = "HomeWin", Odds = match.HomeWinOdds, Probability = match.HomeWinProbability });
                }
                if (match.AwayWinProbability >= minProb)
                {
                    result.Add(new MatchSelection { Match = match, BetType = "AwayWin", Odds = match.AwayWinOdds, Probability = match.AwayWinProbability });
                }
            }
            // Add other types here if needed (BTTS, Over25)
        }

        return result.OrderByDescending(s => s.Probability).Take(20).ToList(); // Step 3 optimization: Limit pool
    }

    private List<Combination> GenerateRecursive(List<MatchSelection> selections, int size)
    {
        var results = new List<Combination>();
        GenerateRecursiveInternal(selections, size, 0, new List<MatchSelection>(), results, new HashSet<string>());
        return results;
    }

    private void GenerateRecursiveInternal(
        List<MatchSelection> pool, 
        int size, 
        int start, 
        List<MatchSelection> current, 
        List<Combination> results, 
        HashSet<string> usedTeams)
    {
        if (current.Count == size)
        {
            var totalOdds = current.Aggregate(1.0, (acc, s) => acc * s.Odds);
            results.Add(new Combination
            {
                Matches = new List<MatchSelection>(current),
                TotalOdds = Math.Round(totalOdds, 2),
                AvgProbability = Math.Round(current.Average(s => s.Probability), 2)
            });
            return;
        }

        for (int i = start; i < pool.Count; i++)
        {
            var selection = pool[i];
            
            // Constraint: No team appears twice
            if (usedTeams.Contains(selection.Match.HomeTeam) || usedTeams.Contains(selection.Match.AwayTeam))
                continue;

            current.Add(selection);
            usedTeams.Add(selection.Match.HomeTeam);
            usedTeams.Add(selection.Match.AwayTeam);

            GenerateRecursiveInternal(pool, size, i + 1, current, results, usedTeams);

            // Backtrack
            usedTeams.Remove(selection.Match.HomeTeam);
            usedTeams.Remove(selection.Match.AwayTeam);
            current.RemoveAt(current.Count - 1);

            if (results.Count >= 50) break; // Optimization: Don't generate thousands of combinations
        }
    }

    private double CalculateScore(Combination combo)
    {
        var avgProb = combo.AvgProbability;
        var avgForm = combo.Matches.Average(m => (m.Match.HomeForm + m.Match.AwayForm) / 2.0);
        
        // Normalize odds: Scale 1.0 - 10.0 to 0 - 1
        var normalizedOdds = Math.Min(combo.TotalOdds / 10.0, 1.0);

        return (avgProb * ProbabilityWeight) + (avgForm * FormWeight) + (normalizedOdds * OddsWeight);
    }

    private string GenerateReasoning(Combination combo)
    {
        var strength = combo.AvgProbability > 0.75 ? "High" : "Strong";
        return $"{strength} probability selections with an average winning chance of {combo.AvgProbability:P0}. Combined with robust recent form across all teams, this portfolio offers statistical value at {combo.TotalOdds:F2} total odds.";
    }
}
