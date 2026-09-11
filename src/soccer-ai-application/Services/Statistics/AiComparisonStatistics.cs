using System.Text.Json;
using System.Text.Json.Serialization;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Decisions;

namespace SoccerAi.Application.Services.Statistics;

public sealed record AiPickRate(int Picks, int Correct, double? HitRate, double? Coverage, double? Lower95, double? Upper95);
public sealed record AiMarketComparison(string Market, int Opportunities, AiPickRate ModelOnly, AiPickRate Combined,
    AiPickRate StrictAgreement, AiPickRate AddedByAi, AiPickRate RemovedByAi);
public sealed record AiComparisonStatistic(int RecordedFixtures, int ComparableFixtures, int MissingAiFixtures,
    int UnverifiedAiFixtures, int StaleOddsFixtures, List<AiMarketComparison> Markets, string[] AiModels, string[] Policies, string Note);

/// <summary>Scores the actual AI policy and its recorded pre-AI counterfactual on one common population.</summary>
public static class AiComparisonStatistics
{
    private sealed class Capture
    {
        public int Schema { get; init; }
        [JsonPropertyName("live_odds")] public bool LiveOdds { get; init; }
        public AiAnalysisDto? Ai { get; init; }
        public DecisionAudit? Audit { get; init; }
    }
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static AiComparisonStatistic Build(IEnumerable<(Fixture Fixture, PredictionSnapshot Snapshot)> pairs)
    {
        int recorded = 0, missing = 0, unverified = 0, stale = 0;
        var comparable = new List<(Fixture Fixture, Capture Capture)>();
        foreach (var (fixture, snapshot) in pairs)
        {
            Capture? context;
            try { context = JsonSerializer.Deserialize<Capture>(snapshot.ContextJson, Json); }
            catch (JsonException) { continue; }
            if (context is not { Schema: 2, Audit: not null }) continue;
            recorded++;
            if (context.Ai is not { HasDecisionLayer: true } ai) { missing++; continue; }
            if (ai.GeneratedAtUtc is not { } generated || generated > snapshot.CapturedAtUtc || generated >= fixture.Date ||
                string.IsNullOrWhiteSpace(ai.ModelVersion) || string.IsNullOrWhiteSpace(ai.PromptHash) || string.IsNullOrWhiteSpace(ai.InputHash))
            { unverified++; continue; }
            if (!context.LiveOdds) { stale++; continue; }
            if (context.Audit.Markets is not { Count: > 0 } ||
                context.Audit.ComputedAtUtc == default || context.Audit.ComputedAtUtc > snapshot.CapturedAtUtc ||
                context.Audit.Markets.GroupBy(m => m.Market).Any(g => g.Count() != 1) ||
                context.Audit.Markets.Any(m => m.ModelOnlyQualified is null)) { unverified++; continue; }
            comparable.Add((fixture, context));
        }
        var markets = new List<AiMarketComparison>();
        foreach (var key in new[] { "btts", "over25", "under25", "match_winner", "goals_2_3" })
        {
            var rows = comparable.SelectMany(x => x.Capture.Audit!.Markets.Where(m => m.Market == key && m.AiAgrees is not null)
                .Select(m => (Market: m, Hit: Won(key, m.Selection, x.Fixture)))).ToList();
            // Re-apply the live price floor for every arm; opinions cannot bypass it.
            bool Model(MarketRuleAudit m) => Priced(m) && m.ModelOnlyQualified == true;
            bool Combined(MarketRuleAudit m) => Priced(m) && m.Qualified;
            AiPickRate Rate(Func<MarketRuleAudit, bool> choose)
            {
                var picks = rows.Where(r => choose(r.Market)).ToList();
                var correct = picks.Count(r => r.Hit); var (lo, hi) = PredictionStatisticsService.Wilson(correct, picks.Count);
                return new(picks.Count, correct, picks.Count == 0 ? null : (double)correct / picks.Count,
                    rows.Count == 0 ? null : (double)picks.Count / rows.Count, lo, hi);
            }
            markets.Add(new(key, rows.Count, Rate(Model), Rate(Combined), Rate(m => Model(m) && m.AiAgrees == true),
                Rate(m => !Model(m) && Combined(m)), Rate(m => Model(m) && !Combined(m))));
        }
        return new(recorded, comparable.Count, missing, unverified, stale, markets,
            comparable.Select(x => x.Capture.Ai!.ModelVersion!).Distinct().Order().ToArray(),
            comparable.SelectMany(x => x.Capture.Audit!.Markets).Select(m => m.AiAgreementMode ?? "unknown").Distinct().Order().ToArray(),
            "T-1h recorded snapshots; same AI-available, freshly priced fixtures for all arms. Hit rate is precision of selected markets, " +
            "not accuracy over every match. Strict agreement is a shadow filter, not necessarily the live policy. " +
            "AI changes qualification, not probabilities. Added/removed picks expose its marginal decisions. " +
            "AI saw model inputs, so agreement is not independent evidence. Missing provenance is excluded; no historical AI is regenerated. " +
            "Intervals are nominal; differences in hit rate also reflect changes in coverage and match difficulty, not causal lift.");
    }
    private static bool Priced(MarketRuleAudit m) => double.IsFinite(m.Probability) && m.Probability is >= 0 and <= 1 &&
        m.Odds is { } odds && double.IsFinite(odds) &&
        odds >= Math.Max(LiveOddsPolicy.MinimumOdds, m.MinOdds) && m.Probability * odds > 1 &&
        m.Probability * odds - 1 >= m.MinEdge;
    private static bool Won(string market, string selection, Fixture f) => market switch
    {
        "btts" => f.HomeGoal > 0 && f.AwayGoal > 0,
        "over25" => f.HomeGoal + f.AwayGoal > 2,
        "under25" => f.HomeGoal + f.AwayGoal <= 2,
        "goals_2_3" => f.HomeGoal + f.AwayGoal is 2 or 3,
        "match_winner" when selection == ConfluenceRuleEngine.Selections.MatchWinnerHome => f.HomeGoal > f.AwayGoal,
        "match_winner" when selection == ConfluenceRuleEngine.Selections.MatchWinnerAway => f.AwayGoal > f.HomeGoal,
        _ => false
    };
}
