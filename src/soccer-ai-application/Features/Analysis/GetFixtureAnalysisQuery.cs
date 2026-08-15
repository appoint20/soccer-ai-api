using Mediator.Net.Contracts;

namespace SoccerAi.Application.Features.Analysis;

/// <summary>
/// One fixture's analysis, looked up by id rather than by date.
///
/// The date-scoped list endpoint forced the app to find a fixture inside
/// "today", so opening tomorrow's match reported it as missing from today's
/// analysis. A fixture id is unambiguous and works for any date.
/// </summary>
public sealed class GetFixtureAnalysisQuery : IRequest
{
    public int FixtureId { get; set; }
    public string Language { get; set; } = "en";

    /// <summary>Admin-only recompute. Slow — never send from the app.</summary>
    public bool Refresh { get; set; }
}

public sealed class GetFixtureAnalysisResponse : IResponse
{
    /// <summary>Null when the fixture does not exist or has no analysis.</summary>
    public Models.MatchAnalysis? Match { get; set; }

    /// <summary>Forecasts recorded for this fixture, empty when none.</summary>
    public List<MatchModelForecastDto> ModelForecasts { get; set; } = [];
}
