using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface ISignalBuilderService
{
    AnalyticalSignals Build(PoissonModel poisson, MonteCarloModel monteCarlo, HeadToHeadModel h2h);
}
