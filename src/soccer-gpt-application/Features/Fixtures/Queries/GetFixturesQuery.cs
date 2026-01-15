using Mediator.Net.Contracts;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.Fixtures.Queries;

public class GetFixturesQuery : IRequest
{
    public bool Upcoming { get; init; } = true;
    public int Limit { get; init; } = 50;
    public int Offset { get; init; } = 0;
}

public class GetFixturesResponse : IResponse
{
    public PagedResponse<FixtureDto> Data { get; init; }
}

public class FixtureDto
{
    public int Id { get; set; }
    public DateTime Date { get; set; }
    public string Time { get; set; } = string.Empty;
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;
    public double? HomeWinOdds { get; set; }
    public double? DrawOdds { get; set; }
    public double? AwayWinOdds { get; set; }
    public double? Over25Odds { get; set; }
    public double? BttsOdds { get; set; }
}
