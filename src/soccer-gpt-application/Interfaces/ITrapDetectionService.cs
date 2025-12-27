using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface ITrapDetectionService
{
    List<string> AnalyzeTraps(UpcomingMatchDto match, PoissonProbabilitiesDto analytics);
}
