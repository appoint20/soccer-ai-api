using SoccerAi.Application.Features.Combinations;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Application.Services.Combinations;

public sealed class ChatCombinationEngine : IChatCombinationEngine
{
    private const double ProbabilityWeight = 0.6;
    private const double FormWeight = 0.2;
    private const double ValueWeight = 0.2;

    public List<CombinationDto> GenerateCombinations(List<MatchAnalysis> matches, ChatCombinationIntent intent)
    {
        var sizes = intent.SourceType == "USER" 
            ? Enumerable.Range(intent.MinMatches, Math.Max(1, intent.MaxMatches - intent.MinMatches + 1)).ToArray()
            : new[] { 2 }; // Strictly bet Doubles to multiply win-rate and reduce variance for System run
            
        var finalCombinations = new List<CombinationDto>();
        var globallyUsedMatchIds = new HashSet<int>();
        var totalGoals23Count = 0;

        // 1. Pre-filter all matches into candidates
        // We ensure we have a robust pool of candidates
        var allCandidates = FilterAllPossibleCandidates(matches, intent);

        // Define our 5 slots with specific rules
        for (int i = 0; i < 5; i++)
        {
            // Slot rules:
            // 0, 1, 2: ONLY BTTS and Over 2.5 Goals
            // 3, 4: Mixed, but must include AT LEAST ONE 'Win' market (HomeWin, AwayWin, Draw)
            bool isGoalOnlySlot = i < 3;
            int requiredMatchCount = 2; // Every combo is a double

            // Filter the available pool for this specific slot
            var slotCandidates = allCandidates
                .Where(c => !globallyUsedMatchIds.Contains(c.Id))
                .Where(c => !isGoalOnlySlot || (c.Market == "BTTS" || c.Market == "Over25"))
                // Enforce global 2-3 Goals limit (Max 1 across all 10 selections)
                .Where(c => c.Market != "Goals23" || totalGoals23Count < 1)
                .OrderByDescending(c => c.Score)
                .ToList();

            if (slotCandidates.Count < requiredMatchCount) continue;

            List<CandidateMatch>? selectedCombo = null;

            if (isGoalOnlySlot)
            {
                // Simple best double for Goal slots
                var possibleGoalCombos = BuildRecursive(slotCandidates, requiredMatchCount, Math.Max(intent.MaxSameLeague, 3));
                selectedCombo = possibleGoalCombos.FirstOrDefault();
            }
            else
            {
                // For mixed slots, we want a combination that contains at least one Win market
                var allMixedCombos = BuildRecursive(slotCandidates, requiredMatchCount, Math.Max(intent.MaxSameLeague, 3));
                selectedCombo = allMixedCombos.FirstOrDefault(c => c.Any(m => m.Market == "HomeWin" || m.Market == "AwayWin" || m.Market == "Draw"));

                // Fallback: If top combos don't have a Win, force one by seeding the recursive builder with a top Win candidate
                if (selectedCombo == null)
                {
                    var topWin = slotCandidates.FirstOrDefault(m => m.Market == "HomeWin" || m.Market == "AwayWin" || m.Market == "Draw");
                    if (topWin != null)
                    {
                        var forcedCombos = BuildRecursive(slotCandidates, requiredMatchCount, Math.Max(intent.MaxSameLeague, 3), new List<CandidateMatch> { topWin });
                        selectedCombo = forcedCombos.FirstOrDefault();
                    }
                }
            }

            if (selectedCombo != null && selectedCombo.Count == requiredMatchCount)
            {
                finalCombinations.Add(MapToDto(selectedCombo, intent, requiredMatchCount));
                
                // Track usage
                foreach (var m in selectedCombo)
                {
                    globallyUsedMatchIds.Add(m.Id);
                    if (m.Market == "Goals23") totalGoals23Count++;
                }
            }
        }

        // Final ID assignment
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
            var validSelections = GetValidSelections(m, allowedMarkets.ToList(), intent.MinSelectionOdds, intent);
            foreach (var sel in validSelections)
            {
                var prob = sel.Probability;
                var form = CalculateFormScore(m);
                var val = (sel.Probability * sel.Odds) / 2.0;

                // --- MARKET HIERARCHY ---
                // Primary (Goal Atmosphere): BTTS, Over25, BttsAndOver25. Weight 1.2x
                // Secondary (Match State): Wins. Weight 1.0x
                // Low Priority: 2-3 Goals. Weight 0.8x
                double hierarchyWeight = (sel.Market == "BTTS" || sel.Market == "Over25" || sel.Market == "BttsAndOver25") ? 1.2 : (sel.Market == "Goals23" ? 0.8 : 1.0);
                
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
        "BttsAndOver25" => "BTTS & Over 2.5 Goals",
        _ => market
    };

    private List<(string Market, double Odds, double Probability, bool IsLowValue)> GetValidSelections(MatchAnalysis m, List<string> requested, double minOdds, ChatCombinationIntent intent)
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

        // --- SMART TRAP AVOIDANCE ---
        // If Gemini explicitly flags this match as a trap/unpredictable, do not touch it!
        if (m.Trap != null && m.Trap.IsTrap)
        {
            return res;
        }

        // --- EXPECTED VALUE (EV) ROI RULES ---
        foreach (var item in map)
        {
            if (!requested.Contains(item.Key)) continue;

            // IF USER REQUEST: We trust the user's minOdds prompt blindly and bypass System EV Math constraints
            if (intent.SourceType == "USER")
            {
                if (item.Odds >= minOdds) res.Add((item.Key, item.Odds, item.Prob, false));
                continue;
            }

            // --- SYSTEM GENERATION ROI RULES ---
            
            // 1. Minimum baseline probability: Require HIGH certainty (Bankers only)
            if (item.Prob < 0.45) continue;

            // 2. The Extreme Value Strategy 
            // In order to achieve the high +60% ROI targets, we MUST focus entirely on gross market mispricings.
            double expectedValue = item.Odds * item.Prob;
            if (expectedValue < 1.15) continue;

            bool meetsFloor = item.Odds >= minOdds;

            // 3. Accept strictly based on EV edge
            if (meetsFloor || expectedValue >= 1.20)
            {
                res.Add((item.Key, item.Odds, item.Prob, false));
            }
        }

        // --- BTTS + Over 2.5 COMBINED EXCEPTION ---
        if (requested.Contains("BTTS") || requested.Contains("Over25"))
        {
            double bttsProb = (m.Prediction?.BTTS.Probability ?? 0) * 0.90;
            double over25Prob = (m.Prediction?.Over25.Probability ?? 0) * 0.90;

            if (bttsProb > 0.50 && over25Prob > 0.50)
            {
                double combinedOdds = Math.Max(m.OddsBttsYes, m.OddsOver25) * 1.30;
                double minProb = Math.Min(bttsProb, over25Prob);

                if (combinedOdds >= minOdds && (combinedOdds * minProb) >= 1.00 && !res.Any(r => r.Item1 == "BttsAndOver25"))
                {
                    res.Add(("BttsAndOver25", Math.Round(combinedOdds, 2), minProb, false));
                }
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

    private List<List<CandidateMatch>> BuildRecursive(List<CandidateMatch> candidates, int size, int maxLeague, List<CandidateMatch>? seed = null)
    {
        var results = new List<List<CandidateMatch>>();
        var startingPool = seed ?? new List<CandidateMatch>();
        
        // When using a seed (forced matches), we need to ensure the starting used teams/leagues are respected
        var usedTeams = new HashSet<string>();
        var leagueCounts = new Dictionary<string, int>();

        foreach (var s in startingPool)
        {
            usedTeams.Add(s.HomeTeam);
            usedTeams.Add(s.AwayTeam);
            leagueCounts[s.League] = leagueCounts.GetValueOrDefault(s.League) + 1;
        }

        // Filter candidates to avoid re-selecting the same match ID that is in the seed
        var seedIds = startingPool.Select(s => s.Id).ToHashSet();
        var pool = candidates.Where(c => !seedIds.Contains(c.Id)).ToList();

        GenerateCombinationsRecursive(pool, size, 0, startingPool, results, usedTeams, leagueCounts, maxLeague);
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
