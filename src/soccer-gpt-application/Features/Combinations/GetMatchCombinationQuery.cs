using Mediator.Net.Contracts;

namespace soccer_gpt_application.Features.Combinations;

public class GetMatchCombinationQuery(DateTime date, string language = "en") : IRequest
{
    public DateTime Date { get; } = date;
    public string Language { get; } = language;
}

public class GetMatchCombinationResponse(List<CombinationDto> combinations) : IResponse
{
    public List<CombinationDto> Combinations { get; } = combinations;
}

public record CombinationDto(
    string Name,
    List<CombinationMatchDto> Matches
);

public record CombinationMatchDto(
    int FixtureId,
    int LeagueId,
    string LeagueName,
    DateTime MatchDate,
    string HomeTeam,
    string AwayTeam,
    string Market, 
    string Prediction, 
    double Confidence,
    double Odds, 
    string? Status,
    int? ActualHomeGoals,
    int? ActualAwayGoals,
    bool IsTrap,
    bool IsConsensus,
    bool IsFallback,
    string? TrapReason,
    double ExpectedValue = 0,
    string Decision = "NoBet",
    string? GeminiRecommendation = null,
    double GeminiConfidence = 0,
    bool GeminiIsTrap = false,
    string? GeminiTrapReason = null,
    string? GeminiOneLineSummary = null,
    string? GeminiReasoning = null,   // Maps to frontend m.gemini_reasoning
    string? GeminiAnalysisText = null // Maps to frontend m.gemini_analysis_text
);
