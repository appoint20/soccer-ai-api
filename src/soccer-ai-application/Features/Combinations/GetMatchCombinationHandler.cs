using Mediator.Net.Context;
using Mediator.Net.Contracts;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Services.Decisions;

namespace SoccerAi.Application.Features.Combinations;

/// <summary>
/// Builds the day's combination portfolios.
///
/// Selection is statistical end to end: Dixon-Coles probabilities, isotonic
/// calibration, the confluence gate, then <see cref="TicketBuilder"/> for
/// pricing and shape. The language model is not consulted here and never was
/// allowed to be — it writes narrative text elsewhere, it does not choose bets.
///
/// Before this handler was rewritten it delegated selection to
/// <c>IChatCombinationEngine</c>, which asked an LLM to assemble the portfolios.
/// That produced combinations nobody could backtest, and returned nothing at all
/// when no model API key was configured.
/// </summary>
public class GetMatchCombinationHandler(
    IDailyPickService pickService,
    ILogger<GetMatchCombinationHandler> logger)
    : IRequestHandler<GetMatchCombinationQuery, GetMatchCombinationResponse>
{
    public async Task<GetMatchCombinationResponse> Handle(
        IReceiveContext<GetMatchCombinationQuery> context,
        CancellationToken cancellationToken)
    {
        var query = context.Message;
        var date = DateOnly.FromDateTime(query.Date.UtcDateTime);

        var board = await pickService.GetBoardAsync(date, query.Language, cancellationToken);

        var combinations = board.Tickets
            .Select((ticket, index) => ToDto(ticket, index + 1, board.Fixtures))
            .ToList();

        logger.LogInformation(
            "[Combinations] {Date}: {Count} portfolios from {Priced}/{Fixtures} priced fixtures",
            date, combinations.Count, board.Coverage.Priced, board.Coverage.Fixtures);

        return new GetMatchCombinationResponse(
            combinations, query.ResolveLimit(), query.ResolveOffset());
    }

    private static CombinationDto ToDto(
        Ticket ticket, int id, IReadOnlyDictionary<int, FixtureRef> fixtures) =>
        new()
        {
            CombinationId = id,
            Type = TypeOf(ticket),
            SourceType = "SYSTEM",
            TotalOdds = ticket.TotalOdds,
            TotalCount = ticket.Legs.Count,
            Reason = DescribeValue(ticket),
            Matches = [.. ticket.Legs.Select(leg => ToDto(leg, fixtures.GetValueOrDefault(leg.FixtureId)))]
        };

    private static string TypeOf(Ticket ticket) =>
        ticket.IsSameMatchPair ? "same_match_pair"
        : ticket.IsSingle ? "single"
        : $"{ticket.Legs.Count}_leg_combo";

    /// <summary>
    /// States the edge in the terms it was actually computed in, so a reader can
    /// check the claim: price offered versus price the model considers fair.
    /// </summary>
    private static string DescribeValue(Ticket ticket) =>
        $"Model probability {ticket.CombinedProbability:P1} implies a fair price of "
        + $"{ticket.FairOdds:0.00}; the offered {ticket.TotalOdds:0.00} carries "
        + $"{ticket.Ev:P1} expected value.";

    private static CombinationMatchDto ToDto(TicketLeg leg, FixtureRef? fixture) =>
        new()
        {
            FixtureId = leg.FixtureId,
            League = leg.League,
            HomeTeam = fixture?.HomeTeam ?? string.Empty,
            AwayTeam = fixture?.AwayTeam ?? string.Empty,
            Selection = leg.Selection,
            Odds = leg.Odds,
            Confidence = Math.Round(leg.Probability, 4),
            Reasoning = $"EV {leg.Ev:P1} at {leg.Odds:0.00}."
        };
}
