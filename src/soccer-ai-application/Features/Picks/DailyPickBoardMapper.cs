using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services.Decisions;

namespace SoccerAi.Application.Features.Picks;

/// <summary>
/// Maps the domain pick board onto the wire contract. Kept apart from the
/// handler so the shape of the API can change without touching selection logic.
/// </summary>
public static class DailyPickBoardMapper
{
    public static GetDailyPicksResponse ToResponse(DailyPickBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);

        var tickets = board.Tickets.Select(t => ToDto(t, board.Fixtures)).ToList();

        return new GetDailyPicksResponse
        {
            Date = board.Date,
            Singles = [.. tickets.Where(t => t.Kind == TicketDto.Kinds.Single)],
            SameMatchPairs = [.. tickets.Where(t => t.Kind == TicketDto.Kinds.SameMatchPair)],
            Combos = [.. tickets.Where(t => t.Kind == TicketDto.Kinds.Combo)],
            ConfidencePicks = [.. board.ConfidencePicks.Select(ToDto)],
            Coverage = new PickCoverageDto
            {
                Fixtures = board.Coverage.Fixtures,
                Analyzed = board.Coverage.Analyzed,
                Priced = board.Coverage.Priced,
                PricedPct = Math.Round(board.Coverage.PricedShare * 100, 1)
            }
        };
    }

    private static TicketDto ToDto(Ticket ticket, IReadOnlyDictionary<int, FixtureRef> fixtures) =>
        new()
        {
            Kind = KindOf(ticket),
            Legs = [.. ticket.Legs.Select(l => ToDto(l, fixtures.GetValueOrDefault(l.FixtureId)))],
            TotalOdds = ticket.TotalOdds,
            FairOdds = ticket.FairOdds,
            Probability = ticket.CombinedProbability,
            Ev = ticket.Ev,
            KellyStake = ticket.KellyStake,
            ContainsGoalsMarket = ticket.ContainsGoalsMarket
        };

    private static string KindOf(Ticket ticket) =>
        ticket.IsSameMatchPair ? TicketDto.Kinds.SameMatchPair
        : ticket.IsSingle ? TicketDto.Kinds.Single
        : TicketDto.Kinds.Combo;

    private static PickLegDto ToDto(TicketLeg leg, FixtureRef? fixture) =>
        new()
        {
            FixtureId = leg.FixtureId,
            KickoffUtc = fixture?.KickoffUtc ?? default,
            League = leg.League,
            Match = fixture?.Match ?? string.Empty,
            Market = leg.Market,
            Selection = leg.Selection,
            Probability = Math.Round(leg.Probability, 4),
            Odds = leg.Odds,
            Ev = Math.Round(leg.Ev, 4)
        };

    private static ConfidencePickDto ToDto(ConfidencePick pick) =>
        new()
        {
            FixtureId = pick.Fixture.FixtureId,
            KickoffUtc = pick.Fixture.KickoffUtc,
            League = pick.Fixture.League,
            Match = pick.Fixture.Match,
            Market = pick.Market,
            Selection = pick.Selection,
            ModelProbability = Math.Round(pick.Probability, 4)
        };
}
