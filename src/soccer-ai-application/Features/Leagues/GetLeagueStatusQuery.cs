using Mediator.Net.Contracts;

namespace SoccerAi.Application.Features.Leagues;

public class GetLeagueStatusQuery : IRequest
{
    public int LeagueId { get; set; }
}

public class GetLeagueStatusResponse : IResponse
{
    public int LeagueId { get; set; }
    public string LeagueName { get; set; } = string.Empty;
    public int TotalFixtures { get; set; }
    public int CurrentSeasonFixtures { get; set; }
    public DateTimeOffset? LastFixtureDate { get; set; }
    public DateTimeOffset? LastUpdatedAt { get; set; }
    public int TeamCount { get; set; }
    public bool HasStandings { get; set; }
}
