namespace soccer_gpt_application.Models;

/// <summary>
/// ML prediction response for a single market.
/// </summary>
public record MarketPrediction(
    string Market,
    bool Prediction,
    double Confidence,
    double[] Probabilities);

/// <summary>
/// Full prediction response for a fixture.
/// </summary>
public record FixturePrediction(
    int FixtureId,
    MarketPrediction Over25,
    MarketPrediction Btts,
    MarketPrediction Goals2To3,
    MarketPrediction Hda);
