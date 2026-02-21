using soccer_gpt_application.Models;
using soccer_gpt_application.Entities;

namespace soccer_gpt_application.Interfaces;

public interface IDecisionService
{
    DecisionServiceResult Evaluate2(TeamStats homeStats, TeamStats awayStats, HeadToHeadModel head2head);
    DecisionServiceResult Evaluate(MatchContext context, TeamStatsResponse teamStats, HeadToHeadModel h2h, WeightedPrediction? prediction, StatisticalModels stats);
}

public class DecisionServiceResult
{
    public QualificationDecisions Markets { get; init; } = new();
    public TrapDecision Trap { get; init; } = new();
    public Qualification Qualification { get; init; } = new();
    public PredictionDecision Decision { get; init; } = PredictionDecision.NoBet;
}
