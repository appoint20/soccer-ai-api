using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

public interface ISignalBuilderService
{
    AnalyticalSignals Build(PoissonModel poisson, MonteCarloModel monteCarlo, HeadToHeadModel h2h);
}
