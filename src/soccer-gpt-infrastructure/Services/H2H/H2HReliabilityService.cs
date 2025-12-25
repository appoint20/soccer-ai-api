using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_infrastructure.Services.H2H;

public class H2HReliabilityService : IH2HReliabilityService
{
    private readonly ILogger<H2HReliabilityService> _logger;
    
    // Season weights - more recent = more important
    private static readonly Dictionary<int, double> SeasonWeights = new()
    {
        { 0, 1.00 },  // Current season
        { 1, 0.75 },  // Last season
        { 2, 0.50 },  // 2 seasons ago
        { 3, 0.25 },  // 3 seasons ago
        { 4, 0.25 }   // 4 seasons ago
    };

    public H2HReliabilityService(ILogger<H2HReliabilityService> logger)
    {
        _logger = logger;
    }

    public H2HResult Evaluate(
        string homeTeam,
        string awayTeam,
        List<HistoricalMatchDto> history,
        string market)
    {
        // Extract H2H matches (max 8, last 4 years)
        var h2hMatches = GetRelevantH2H(homeTeam, awayTeam, history);

        if (h2hMatches.Count < 3)
        {
            return new H2HResult(
                H2HDecision.Allow,
                1.00,
                "Insufficient H2H data (need min 3 matches)"
            );
        }

        // Evaluate based on market type
        return market switch
        {
            "Over 2.5 Goals" => EvaluateOver25(h2hMatches),
            "BTTS Yes" => EvaluateBTTS(h2hMatches, homeTeam, awayTeam, history),
            "2-3 Goals" => Evaluate2to3Goals(h2hMatches),
            "Home Win" => EvaluateHomeWin(h2hMatches, homeTeam),
            "Away Win" => EvaluateAwayWin(h2hMatches, awayTeam),
            "Draw" => new H2HResult(H2HDecision.Allow, 1.00, "Draw market - H2H skipped"),
            _ => new H2HResult(H2HDecision.Allow, 1.00, $"Unknown market: {market}")
        };
    }

    private List<HistoricalMatchDto> GetRelevantH2H(
        string homeTeam,
        string awayTeam,
        List<HistoricalMatchDto> history)
    {
        var currentDate = DateTime.Now;
        var fourYearsAgo = currentDate.AddYears(-4);

        return history
            .Where(m =>
                m.Date >= fourYearsAgo &&
                ((IsMatch(m.HomeTeam, homeTeam) && IsMatch(m.AwayTeam, awayTeam)) ||
                 (IsMatch(m.HomeTeam, awayTeam) && IsMatch(m.AwayTeam, homeTeam))))
            .OrderByDescending(m => m.Date)
            .Take(8)
            .ToList();
    }

    private H2HResult EvaluateOver25(List<HistoricalMatchDto> h2hMatches)
    {
        // H2H filter disabled per user request - no dampening applied
        return new H2HResult(
            H2HDecision.Allow,
            1.00,
            "H2H filter disabled"
        );
    }

    private H2HResult EvaluateBTTS(
        List<HistoricalMatchDto> h2hMatches,
        string homeTeam,
        string awayTeam,
        List<HistoricalMatchDto> allHistory)
    {
        // H2H filter disabled per user request - no dampening applied
        return new H2HResult(
            H2HDecision.Allow,
            1.00,
            "H2H filter disabled"
        );
    }

    private H2HResult Evaluate2to3Goals(List<HistoricalMatchDto> h2hMatches)
    {
        // H2H filter disabled per user request - no dampening applied
        return new H2HResult(
            H2HDecision.Allow,
            1.00,
            "H2H filter disabled"
        );
    }

    private H2HResult EvaluateHomeWin(List<HistoricalMatchDto> h2hMatches, string homeTeam)
    {
        double weightedHomeWinRate = CalculateWeightedRate(
            h2hMatches,
            m =>
            {
                bool homeIsHome = IsMatch(m.HomeTeam, homeTeam);
                return homeIsHome ? m.FTR == "H" : m.FTR == "A";
            }
        );

        if (weightedHomeWinRate < 0.40)
        {
            _logger.LogInformation(
                "H2H | Market:HomeWin Decision:Dampened Multiplier:0.90 Reason:Poor H2H record {Rate:P0}",
                weightedHomeWinRate
            );
            return new H2HResult(
                H2HDecision.Dampened,
                0.90,
                $"Poor H2H home win rate: {weightedHomeWinRate:P0}"
            );
        }

        return new H2HResult(
            H2HDecision.Allow,
            1.00,
            $"H2H home win rate acceptable: {weightedHomeWinRate:P0}"
        );
    }

    private H2HResult EvaluateAwayWin(List<HistoricalMatchDto> h2hMatches, string awayTeam)
    {
        double weightedAwayWinRate = CalculateWeightedRate(
            h2hMatches,
            m =>
            {
                bool awayIsAway = IsMatch(m.AwayTeam, awayTeam);
                return awayIsAway ? m.FTR == "A" : m.FTR == "H";
            }
        );

        if (weightedAwayWinRate < 0.40)
        {
            _logger.LogInformation(
                "H2H | Market:AwayWin Decision:Dampened Multiplier:0.90 Reason:Poor H2H record {Rate:P0}",
                weightedAwayWinRate
            );
            return new H2HResult(
                H2HDecision.Dampened,
                0.90,
                $"Poor H2H away win rate: {weightedAwayWinRate:P0}"
            );
        }

        return new H2HResult(
            H2HDecision.Allow,
            1.00,
            $"H2H away win rate acceptable: {weightedAwayWinRate:P0}"
        );
    }

    private double CalculateWeightedRate(
        List<HistoricalMatchDto> matches,
        Func<HistoricalMatchDto, bool> predicate)
    {
        var currentDate = DateTime.Now;
        double totalWeight = 0;
        double weightedSuccess = 0;

        foreach (var match in matches)
        {
            int seasonOffset = CalculateSeasonOffset(match.Date, currentDate);
            double weight = SeasonWeights.TryGetValue(seasonOffset, out var w) ? w : 0.25;

            totalWeight += weight;
            if (predicate(match))
            {
                weightedSuccess += weight;
            }
        }

        return totalWeight > 0 ? weightedSuccess / totalWeight : 0;
    }

    private int CalculateSeasonOffset(DateTime matchDate, DateTime currentDate)
    {
        // Simple year difference for now (could be refined with season start dates)
        int yearDiff = currentDate.Year - matchDate.Year;
        return Math.Min(yearDiff, 4); // Cap at 4
    }

    private bool IsMatch(string team1, string team2)
    {
        if (string.IsNullOrWhiteSpace(team1) || string.IsNullOrWhiteSpace(team2)) return false;
        return string.Equals(team1.Trim(), team2.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
