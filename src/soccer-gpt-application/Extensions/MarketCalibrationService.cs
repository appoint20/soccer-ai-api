namespace soccer_gpt_application.Services;

public static class MarketCalibrationService
{
    public static double OddsToProbability(this double odds)
    {
        if (odds <= 0) return 0;
        return 1.0 / odds;
    }

    public static Dictionary<string, double> RemoveMargin(this Dictionary<string, double> probs)
    {
        var total = probs.Values.Sum();
        return probs.ToDictionary(x => x.Key, x => x.Value / total);
    }

    public static double BayesianUpdate(double modelProb, double marketProb, double modelWeight = 0.7)
    {
        return modelWeight * modelProb +
               (1 - modelWeight) * marketProb;
    }
}