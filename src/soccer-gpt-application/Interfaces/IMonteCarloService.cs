using soccer_gpt_application.Models;

namespace soccer_gpt_application.Interfaces;

public interface IMonteCarloService
{
    PredictionResult Predict(PoissonProbabilities poissonProbabilities, MarketOdds? market = null);
}
