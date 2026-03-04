using SoccerAi.Application.Models;

namespace SoccerAi.Application.Interfaces;

public interface IMonteCarloService
{
    PredictionResult Predict(PoissonProbabilities poissonProbabilities, MarketOdds? market = null);
}
