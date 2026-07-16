using Microsoft.Extensions.Options;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Models.Signals;
using SoccerAi.Application.Options;
using SoccerAi.Application.Services.Decisions;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Decision layer v2 — thin adapter around the transparent ConfluenceRuleEngine.
///
/// Input: calibrated DC probabilities (the ONLY probability source) plus the
/// strategic signal catalog. Output: per-market qualifications with a full
/// audit trail of which confirm/veto rules fired.
///
/// No scoring engines, no additive boosts, no EV heuristics, no LLM influence.
/// </summary>
public sealed class DecisionService(
    ILeagueTierService leagueTiers,
    IOptions<ConfluenceOptions> options) : IDecisionService
{
    public Task<DecisionServiceResult> Evaluate(
        MatchContext context,
        TeamStatsResponse teamStats,
        HeadToHeadModel h2h,
        WeightedPrediction? prediction,
        StatisticalModels stats,
        StrategicSignals? signals,
        AiAnalysisDto? aiContext = null)
    {
        if (prediction == null || signals == null)
        {
            return Task.FromResult(new DecisionServiceResult
            {
                Markets = new QualificationDecisions(),
                Trap = new TrapDecision(),
                Qualification = new Qualification
                {
                    IsQualified = false,
                    CombinedProbability = 0,
                    Label = prediction == null ? "No prediction available" : "No signals available"
                }
            });
        }

        var opt = options.Value;

        // Tier2 (cup) fixtures demand a higher probability bar.
        var tierExtra = leagueTiers.GetTier(context.LeagueId) == LeagueTier.Tier2
            ? opt.Tier2ExtraProbability
            : 0.0;

        var audit = ConfluenceRuleEngine.Evaluate(prediction, signals, tierExtra, opt);

        var markets = new QualificationDecisions
        {
            BTTS = ToDecision(audit, ConfluenceRuleEngine.Markets.Btts),
            Over25 = ToDecision(audit, ConfluenceRuleEngine.Markets.Over25),
            TwoToThreeGoals = ToDecision(audit, ConfluenceRuleEngine.Markets.Goals23),
            MatchWinner = ToDecision(audit, ConfluenceRuleEngine.Markets.MatchWinner),
            LowScoring = ToDecision(audit, ConfluenceRuleEngine.Markets.Under25),
            Draw = new DrawDecision { IsQualified = false, Score = 0, Label = "Excluded" }
        };

        // Trap is a market signal now — surfaced for the response, and it also
        // acts as a veto inside the winner rules.
        var trap = new TrapDecision
        {
            IsTrap = signals.Market.Trap.Flag,
            Reason = signals.Market.Trap.Flag ? signals.Market.Trap.Label : string.Empty
        };

        var qualifiedMarkets = audit.Markets.Where(m => m.Qualified).ToList();
        var isQualified = qualifiedMarkets.Count > 0;
        var bestProb = qualifiedMarkets.Count > 0 ? qualifiedMarkets.Max(m => m.Probability) : 0;

        var decision = PredictionDecision.NoBet;
        if (trap.IsTrap && !isQualified)
        {
            decision = PredictionDecision.Avoid;
        }
        else if (isQualified)
        {
            var strong = qualifiedMarkets.Any(m =>
                m.ConfirmationsFired >= opt.MinConfirmations + opt.StrongBetExtraConfirms &&
                m.Probability >= m.Threshold + opt.StrongBetExtraProbability);
            decision = strong ? PredictionDecision.StrongBet : PredictionDecision.SmallEdge;
        }

        return Task.FromResult(new DecisionServiceResult
        {
            Markets = markets,
            Trap = trap,
            Qualification = new Qualification
            {
                IsQualified = isQualified,
                CombinedProbability = Math.Round(bestProb, 3),
                Label = isQualified
                    ? $"Qualified ({qualifiedMarkets.Count} market(s) with confluence)"
                    : "Not qualified"
            },
            Decision = decision,
            Audit = audit
        });
    }

    private static MarketDecision ToDecision(DecisionAudit audit, string market)
    {
        var m = audit.Markets.First(x => x.Market == market);

        var firedConfirms = m.Rules.Where(r => r is { Kind: RuleResult.Confirm, Fired: true })
            .Select(r => r.RuleId).ToList();
        var firedVetoes = m.Rules.Where(r => r is { Kind: RuleResult.Veto, Fired: true })
            .Select(r => r.RuleId).ToList();

        var reason = m.Qualified
            ? $"p={m.Probability:P0} ≥ {m.Threshold:P0}; confirms: {string.Join(", ", firedConfirms)}"
            : !m.ProbabilityPassed
                ? $"p={m.Probability:P0} below threshold {m.Threshold:P0}"
                : firedVetoes.Count > 0
                    ? $"Vetoed: {string.Join(", ", firedVetoes)}"
                    : $"Only {m.ConfirmationsFired} confirm(s), need {audit.MinConfirmationsRequired}";

        return new MarketDecision
        {
            IsQualified = m.Qualified,
            Confidence = m.Probability,
            Reason = reason
        };
    }
}
