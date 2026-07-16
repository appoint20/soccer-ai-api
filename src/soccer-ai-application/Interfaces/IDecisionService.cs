using SoccerAi.Application.Models;
using SoccerAi.Application.Models.Signals;

namespace SoccerAi.Application.Interfaces;

public interface IDecisionService
{
    /// <summary>
    /// Confluence decision: calibrated probabilities + strategic signals in,
    /// audited market qualifications out. Signals gate — never modify — probabilities.
    /// </summary>
    Task<DecisionServiceResult> Evaluate(
        MatchContext context,
        TeamStatsResponse teamStats,
        HeadToHeadModel h2h,
        WeightedPrediction? prediction,
        StatisticalModels stats,
        StrategicSignals? signals,
        AiAnalysisDto? aiContext = null);
}

public class DecisionServiceResult
{
    public QualificationDecisions Markets { get; set; } = new();
    public TrapDecision Trap { get; set; } = new();
    public Qualification Qualification { get; set; } = new();
    public PredictionDecision Decision { get; set; } = PredictionDecision.NoBet;

    /// <summary>Which rules fired per market — persisted in the snapshot.</summary>
    public DecisionAudit? Audit { get; set; }
}
