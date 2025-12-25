using soccer_gpt_application.Interfaces;

namespace soccer_gpt_application.Interfaces;

public interface IH2HReliabilityService
{
    H2HResult Evaluate(
        string homeTeam,
        string awayTeam,
        List<HistoricalMatchDto> history,
        string market
    );
}

public record H2HResult(
    H2HDecision Decision,   // Allow | Dampened | Reject
    double Multiplier,      // 0.75 – 1.00
    string Reason
);

public enum H2HDecision
{
    Allow,      // No change to probability
    Dampened,   // Reduce probability
    Reject      // Block this market
}
