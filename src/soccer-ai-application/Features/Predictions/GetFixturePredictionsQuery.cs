using Mediator.Net.Contracts;

namespace SoccerAi.Application.Features.Predictions;

/// <summary>
/// Query to get ML predictions for upcoming fixtures.
/// </summary>
public class GetFixturePredictionsQuery : IRequest
{
    /// <summary>Date to get predictions for (required)</summary>
    public required DateTimeOffset Date { get; init; }
    
    /// <summary>League ID to filter by (required)</summary>
    public required int LeagueId { get; init; }
    
    /// <summary>Language code (e.g., "en")</summary>
    public required string Language { get; init; }
}

/// <summary>
/// Response containing fixture predictions.
/// </summary>
public class GetFixturePredictionsResponse(DateTimeOffset date, int leagueId, List<FixturePredictionDto> predictions)
    : IResponse
{
    public DateTimeOffset Date { get; } = date;
    public int LeagueId { get; } = leagueId;
    public List<FixturePredictionDto> Predictions { get; } = predictions;
}

/// <summary>
/// Single fixture prediction DTO.
/// </summary>
public record FixturePredictionDto(
    DateTimeOffset MatchDate,
    string Status,
    string HomeTeamName,
    string AwayTeamName,
    int? ActualHomeGoals,
    int? ActualAwayGoals,
    MarketPredictionDto Over25,
    MarketPredictionDto Btts,
    MarketPredictionDto Goals2To3,
    HdaPredictionDto Hda);

/// <summary>
/// Market prediction with confidence.
/// </summary>
public record MarketPredictionDto(
    bool Prediction,
    double Confidence);

/// <summary>
/// H/D/A prediction with probabilities.
/// </summary>
public record HdaPredictionDto(
    string Prediction, // "Home", "Draw", "Away"
    double Confidence,
    double HomeProbability,
    double DrawProbability,
    double AwayProbability);
