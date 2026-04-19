using SoccerAi.Application.Models;
using SoccerAi.Application.Entities;

namespace SoccerAi.Application.Interfaces;

public interface IDecisionService
{
    Task<DecisionServiceResult> Evaluate2(TeamStats homeStats, TeamStats awayStats, HeadToHeadModel head2head);
    Task<DecisionServiceResult> Evaluate(MatchContext context, TeamStatsResponse teamStats, HeadToHeadModel h2h, WeightedPrediction? prediction, StatisticalModels stats);
}

public class DecisionServiceResult
{
    public QualificationDecisions Markets { get; set; } = new();
    public TrapDecision Trap { get; set; } = new();
    public Qualification Qualification { get; set; } = new();
    public PredictionDecision Decision { get; set; } = PredictionDecision.NoBet;
}
