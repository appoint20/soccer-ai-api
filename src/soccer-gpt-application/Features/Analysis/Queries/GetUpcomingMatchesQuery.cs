using Mediator.Net.Contracts;

namespace soccer_gpt_application.Features.Analysis.Queries;

public class GetUpcomingMatchesQuery : IRequest
{
    public int Offset { get; init; } = 0;
    public int Limit { get; init; } = 50;
    public DateTime Date { get; init; }
}