using Mediator.Net.Context;
using Mediator.Net.Contracts;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Application.Features.Picks;

/// <summary>
/// Serves the day's picks. Thin by design: the board is produced by
/// <see cref="IDailyPickService"/>, which shares its selection code with the
/// backtest, so this handler only resolves defaults and maps to the wire shape.
/// </summary>
public sealed class GetDailyPicksHandler(IDailyPickService pickService)
    : IRequestHandler<GetDailyPicksQuery, GetDailyPicksResponse>
{
    public async Task<GetDailyPicksResponse> Handle(
        IReceiveContext<GetDailyPicksQuery> context,
        CancellationToken cancellationToken)
    {
        var query = context.Message;
        var date = query.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var lang = string.IsNullOrWhiteSpace(query.Language) ? "en" : query.Language;

        var board = await pickService.GetBoardAsync(date, lang, cancellationToken);

        return DailyPickBoardMapper.ToResponse(board);
    }
}
