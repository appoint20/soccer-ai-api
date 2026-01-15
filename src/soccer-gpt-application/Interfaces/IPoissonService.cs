using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface IPoissonService
{
    StrengthFactors Build(
        TeamAggregatedStats homeTeamHomeStats,
        TeamAggregatedStats awayTeamAwayStats,
        LeagueGoalAverages leagueAverages);

    PoissonProbabilities CalculateProbabilities(StrengthFactors poissonAnalysis);
}
