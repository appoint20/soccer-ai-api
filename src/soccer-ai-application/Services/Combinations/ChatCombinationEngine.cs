using SoccerAi.Application.Features.Combinations;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Services.Combinations;

public sealed class ChatCombinationEngine : IChatCombinationEngine
{
    private const double ProbabilityWeight = 0.6;
    private const double FormWeight = 0.3;
    private const double ValueWeight = 0.1;

    public List<CombinationDto> GenerateCombinations(List<MatchAnalysis> matches, ChatCombinationIntent intent)
    {
        var finalCombinations = new List<CombinationDto>();
        var globallyUsedMatchIds = new HashSet<int>();

        // 1. Pre-filter all matches into candidates
        var allCandidates = FilterAllPossibleCandidates(matches, intent);

        // --- ROBUSTNESS: Ensure MarketGroups is never empty ---
        if (intent.MarketGroups == null || intent.MarketGroups.Count == 0)
        {
            intent.MarketGroups = new List<MarketIntentGroup>
            {
                new() { MatchCount = 3, Markets = new List<string> { "HomeWin", "AwayWin", "Draw", "BTTS", "Over25", "Goals23" } },
                new() { MatchCount = 2, Markets = new List<string> { "HomeWin", "AwayWin", "Draw", "BTTS", "Over25", "Goals23" } }
            };
        }

        // 2. Sequential Interleaved Generation (to ensure diversity and type representation)
        // We want up to 5 combinations total.
        int maxAttempts = 5;
        for (int i = 0; i < maxAttempts; i++)
        {
            var groupIdx = i % intent.MarketGroups.Count;
            var group = intent.MarketGroups[groupIdx];

            // Filter out already used matches for THIS slot
            var availablePool = allCandidates
                .Where(c => !globallyUsedMatchIds.Contains(c.Id))
                .Where(c => group.Markets.Contains(c.Market))
                .OrderByDescending(c => c.Score)
                .ToList();

            if (availablePool.Count < group.MatchCount) continue;

            // Generate exactly ONE best combination for this group using only fresh matches
            var results = BuildRecursive(availablePool, group.MatchCount, intent.MaxSameLeague);
            if (results.Count > 0)
            {
                var best = results[0]; // Take the highest scoring one
                var dto = MapToDto(best, intent, group.MatchCount);
                finalCombinations.Add(dto);

                // Lock these matches for ALL subsequent combinations
                foreach (var m in best) globallyUsedMatchIds.Add(m.Id);
            }
        }

        // 3. Fallback: If we still haven't reached 5 combinations, try to fill with any remaining matches
        if (finalCombinations.Count < 5)
        {
            var fallbackGroup = intent.MarketGroups.OrderByDescending(g => g.MatchCount).FirstOrDefault();
            if (fallbackGroup != null)
            {
                var remaining = allCandidates
                    .Where(c => !globallyUsedMatchIds.Contains(c.Id))
                    .OrderByDescending(c => c.Score)
                    .ToList();

                var extraResults = BuildRecursive(remaining, fallbackGroup.MatchCount, intent.MaxSameLeague);
                foreach (var res in extraResults.Take(5 - finalCombinations.Count))
                {
                    finalCombinations.Add(MapToDto(res, intent, fallbackGroup.MatchCount));
                    foreach (var m in res) globallyUsedMatchIds.Add(m.Id);
                }
            }
        }

        // 4. Assign IDs and Final Polish
        for (int i = 0; i < finalCombinations.Count; i++)
        {
            finalCombinations[i].CombinationId = i + 1;
        }

        return finalCombinations;
    }

    private CombinationDto MapToDto(List<CandidateMatch> comboMatches, ChatCombinationIntent intent, int size)
    {
        var totalOdds = comboMatches.Aggregate(1.0, (acc, m) => acc * m.Odds);
        var avgScore = comboMatches.Average(m => m.Score);

        return new CombinationDto
        {
            Type = size == 2 ? "DOUBLE" : size == 3 ? "TREBLE" : "ACCUMULATOR",
            SourceType = "USER",
            TotalOdds = Math.Round(totalOdds, 2),
            Matches = comboMatches.Select(m => new CombinationMatchDto
            {
                FixtureId = m.Id,
                League = m.League,
                HomeTeam = m.HomeTeam,
                AwayTeam = m.AwayTeam,
                Selection = MapToDisplayName(m.Market),
                Odds = m.Odds,
                Confidence = m.Probability * 100,
                Reasoning = GetNaturalReasoning(m)
            }).ToList(),
            Reason = $"Custom Portfolio: Generated based on your specific criteria. Selection prioritized for peak value and statistical consensus."
        };
    }

    private string GetNaturalReasoning(CandidateMatch m)
    {
        var formText = m.FormScore switch
        {
            > 0.8 => "exceptional recent form",
            > 0.6 => "strong consistent performance",
            > 0.4 => "balanced recent results",
            _ => "statistical potential"
        };

        var probText = m.Probability switch
        {
            > 0.7 => "high-confidence statistical advantage",
            > 0.5 => "strong mathematical probability",
            _ => "favorable value assessment"
        };

        return $"This selection combines {formText} with a {probText} for the requested market.";
    }

    private List<CandidateMatch> FilterAllPossibleCandidates(List<MatchAnalysis> matches, ChatCombinationIntent intent)
    {
        var list = new List<CandidateMatch>();
        var allowedMarkets = intent.MarketGroups.SelectMany(g => g.Markets).ToHashSet();
        if (!allowedMarkets.Any()) allowedMarkets = new HashSet<string> { "HomeWin", "AwayWin", "Draw", "BTTS", "Over25", "Goals23" };

        foreach (var m in matches)
        {
            var validSelections = GetValidSelections(m, allowedMarkets.ToList(), intent.MinSelectionOdds);
            foreach (var sel in validSelections)
            {
                var prob = sel.Probability;
                var form = CalculateFormScore(m);
                var val = (sel.Probability * sel.Odds) / 2.0;

                // --- MARKET HIERARCHY ---
                // Primary (Goal Atmosphere): BTTS, Over25. Weight 1.2x
                // Secondary (Match State): Wins, 2-3 Goals. Weight 1.0x
                double hierarchyWeight = (sel.Market == "BTTS" || sel.Market == "Over25") ? 1.2 : 1.0;
                
                list.Add(new CandidateMatch
                {
                    Id = m.Id,
                    League = m.League,
                    HomeTeam = m.HomeTeam,
                    AwayTeam = m.AwayTeam,
                    Market = sel.Market,
                    Odds = sel.Odds,
                    Probability = prob,
                    FormScore = form,
                    Score = ((prob * ProbabilityWeight) + (form * FormWeight) + (val * ValueWeight)) * hierarchyWeight,
                    IsLowValue = sel.IsLowValue
                });
            }
        }
        return list;
    }

    private string MapToDisplayName(string market) => market switch
    {
        "HomeWin" => "Match Winner (Home)",
        "AwayWin" => "Match Winner (Away)",
        "Draw" => "Draw",
        "BTTS" => "BTTS",
        "Over25" => "Over 2.5 Goals",
        "Under25" => "Under 2.5 Goals",
        "Goals23" => "2-3 Goals",
        _ => market
    };

    private List<(string Market, double Odds, double Probability, bool IsLowValue)> GetValidSelections(MatchAnalysis m, List<string> requested, double minOdds)
    {
        var res = new List<(string, double, double, bool)>();
        
        var map = new List<(string Key, double Odds, double Prob)> 
        {
            ("HomeWin", m.OddsHomeWin, m.Prediction?.HomeWin.Probability ?? 0),
            ("AwayWin", m.OddsAwayWin, m.Prediction?.AwayWin.Probability ?? 0),
            ("Draw", m.OddsDraw, m.Prediction?.Draw.Probability ?? 0),
            ("BTTS", m.OddsBttsYes, m.Prediction?.BTTS.Probability ?? 0),
            ("Over25", m.OddsOver25, m.Prediction?.Over25.Probability ?? 0),
            ("Goals23", m.OddsGoals23, m.Prediction?.TwoToThreeGoals.Probability ?? 0)
        };

        // --- THE "INSANE" RULES ---
        // 1. Min 1.60 Odd (Hard Floor)
        // 2. Goal Exception: If (BTTS + O25) > 1.85, qualify as a Goal market.
        double goalAtmosphere = m.OddsBttsYes + m.OddsOver25;
        bool isStrongGoalEnvironment = goalAtmosphere > 1.85;

        foreach (var item in map)
        {
            if (!requested.Contains(item.Key)) continue;

            bool isGoalMarket = item.Key == "BTTS" || item.Key == "Over25";
            bool meetsFloor = item.Odds >= 1.60;
            bool meetsGoalAtmosphere = isGoalMarket && isStrongGoalEnvironment;

            if (meetsFloor || meetsGoalAtmosphere)
            {
                res.Add((item.Key, item.Odds, item.Prob, false));
            }
            else
            {
                // If user specifically requested it, allow it with a warning
                res.Add((item.Key, item.Odds, item.Prob, true));
            }
        }

        return res;
    }

    private double CalculateFormScore(MatchAnalysis m)
    {
        double home = ParseForm(m.HomeStats.Form);
        double away = ParseForm(m.AwayStats.Form);
        return (home + away) / 2.0;
    }

    private double ParseForm(string form)
    {
        if (string.IsNullOrWhiteSpace(form)) return 0.5;
        double score = 0;
        foreach (char c in form)
        {
            score += c switch { 'W' => 1.0, 'D' => 0.5, _ => 0.0 };
        }
        return score / Math.Max(1, form.Length);
    }

    private List<List<CandidateMatch>> BuildRecursive(List<CandidateMatch> candidates, int size, int maxLeague)
    {
        var results = new List<List<CandidateMatch>>();
        GenerateCombinationsRecursive(candidates, size, 0, new List<CandidateMatch>(), results, new HashSet<string>(), new Dictionary<string, int>(), maxLeague);
        return results;
    }

    private void GenerateCombinationsRecursive(
        List<CandidateMatch> candidates, 
        int size, 
        int start, 
        List<CandidateMatch> current, 
        List<List<CandidateMatch>> results, 
        HashSet<string> usedTeams, 
        Dictionary<string, int> leagueCounts,
        int maxLeague)
    {
        if (current.Count == size)
        {
            results.Add(new List<CandidateMatch>(current));
            return;
        }

        for (int i = start; i < candidates.Count; i++)
        {
            var match = candidates[i];
            
            // Constraint 1: Teams
            if (usedTeams.Contains(match.HomeTeam) || usedTeams.Contains(match.AwayTeam)) continue;

            // Constraint 2: League Diversity
            leagueCounts.TryGetValue(match.League, out int count);
            if (count >= maxLeague) continue;

            current.Add(match);
            usedTeams.Add(match.HomeTeam);
            usedTeams.Add(match.AwayTeam);
            leagueCounts[match.League] = count + 1;
            
            GenerateCombinationsRecursive(candidates, size, i + 1, current, results, usedTeams, leagueCounts, maxLeague);
            
            // Backtrack
            leagueCounts[match.League] = count;
            usedTeams.Remove(match.HomeTeam);
            usedTeams.Remove(match.AwayTeam);
            current.RemoveAt(current.Count - 1);

            if (results.Count >= 5) break; // We only need a few candidates per inner recursive pass
        }
    }

    private class CandidateMatch
    {
        public int Id { get; set; }
        public string League { get; set; } = "";
        public string HomeTeam { get; set; } = "";
        public string AwayTeam { get; set; } = "";
        public string Market { get; set; } = "";
        public double Odds { get; set; }
        public double Probability { get; set; }
        public double FormScore { get; set; }
        public double Score { get; set; }
        public bool IsLowValue { get; set; }
    }

    private class ScoredCombination
    {
        public double Score { get; set; }
        public CombinationDto Dto { get; set; } = new();
    }
}
