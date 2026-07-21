using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Models.Signals;
using SoccerAi.Application.Options;

namespace SoccerAi.Application.Services.Signals;

/// <summary>
/// Loads pre-kickoff data (histories, H2H, league season, standings, Tier2
/// proximity) and delegates all math to the pure StrategicSignalCalculator.
/// Every query filters Date &lt; kickoff in SQL — no future leakage.
/// </summary>
public sealed class StrategicSignalService(
    IApplicationDbContext dbContext,
    ILeagueTierService leagueTiers,
    ILeagueVolatilityService volatility,
    IOptions<StrategyOptions> options,
    ILogger<StrategicSignalService> logger) : IStrategicSignalService
{
    private const int HistoryDepth = 40; // enough for LongWindow both venue-split sides

    public async Task<StrategicSignals> ComputeAsync(
        Fixture fixture, PoissonModel? dcModel, CancellationToken ct = default)
    {
        try
        {
            var kickoff = fixture.Date;

            var homeHistory = await TeamHistoryAsync(fixture.HomeTeamId, kickoff, ct);
            var awayHistory = await TeamHistoryAsync(fixture.AwayTeamId, kickoff, ct);

            var h2h = await dbContext.Fixtures.AsNoTracking()
                .Where(m => m.Status == "FT" && m.Date < kickoff &&
                            ((m.HomeTeamId == fixture.HomeTeamId && m.AwayTeamId == fixture.AwayTeamId) ||
                             (m.HomeTeamId == fixture.AwayTeamId && m.AwayTeamId == fixture.HomeTeamId)))
                .OrderByDescending(m => m.Date)
                .Take(options.Value.H2HLongWindow)
                .ToListAsync(ct);

            var seasonStart = SeasonStart(kickoff);
            var leagueSeason = await dbContext.Fixtures.AsNoTracking()
                .Where(m => m.LeagueId == fixture.LeagueId && m.Status == "FT" &&
                            m.Date >= seasonStart && m.Date < kickoff)
                .Select(m => new Fixture
                {
                    Id = m.Id, HomeTeamId = m.HomeTeamId, AwayTeamId = m.AwayTeamId,
                    HomeGoal = m.HomeGoal, AwayGoal = m.AwayGoal, Date = m.Date
                })
                .ToListAsync(ct);

            var teams = await dbContext.Teams.AsNoTracking()
                .Where(t => t.ApiId == fixture.HomeTeamId || t.ApiId == fixture.AwayTeamId)
                .ToDictionaryAsync(t => t.ApiId, t => t, ct);

            var homeTier2 = await Tier2WithinAsync(fixture.HomeTeamId, kickoff, ct);
            var awayTier2 = await Tier2WithinAsync(fixture.AwayTeamId, kickoff, ct);

            // Opening-vs-latest drift from timestamped quotes (pre-kickoff only)
            var quotes = await dbContext.FixtureOddsQuotes.AsNoTracking()
                .Where(q => q.FixtureId == fixture.Id && q.CapturedAtUtc < kickoff)
                .ToListAsync(ct);
            var drift = OddsDriftCalculator.Compute(quotes);

            var inputs = new SignalInputs(
                fixture,
                teams.GetValueOrDefault(fixture.HomeTeamId),
                teams.GetValueOrDefault(fixture.AwayTeamId),
                homeHistory,
                awayHistory,
                h2h,
                leagueSeason,
                homeTier2,
                awayTier2,
                dcModel,
                volatility.GetVolatility(fixture.LeagueId),
                drift);

            return StrategicSignalCalculator.Compute(inputs, options.Value);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Strategic signal computation failed for fixture {Id}", fixture.Id);
            return new StrategicSignals { ComputedAtUtc = DateTimeOffset.UtcNow };
        }
    }

    private async Task<List<Fixture>> TeamHistoryAsync(int teamId, DateTimeOffset kickoff, CancellationToken ct) =>
        await dbContext.Fixtures.AsNoTracking()
            .Where(m => m.Status == "FT" && m.Date < kickoff &&
                        (m.HomeTeamId == teamId || m.AwayTeamId == teamId))
            .OrderByDescending(m => m.Date)
            .Take(HistoryDepth)
            .ToListAsync(ct);

    /// <summary>Tier2 (European) match within ±N days of kickoff — played or scheduled.</summary>
    private async Task<bool> Tier2WithinAsync(int teamId, DateTimeOffset kickoff, CancellationToken ct)
    {
        // Tier2 fixtures may exist in the DB even when not in sync scope.
        var tier2Ids = leagueTiers.GetTier2LeagueIds().ToList();
        if (tier2Ids.Count == 0) return false;

        var days = options.Value.Tier2ProximityDays;
        var from = kickoff.AddDays(-days);
        var to = kickoff.AddDays(days);

        return await dbContext.Fixtures.AsNoTracking()
            .AnyAsync(m => (m.HomeTeamId == teamId || m.AwayTeamId == teamId) &&
                           m.Date >= from && m.Date <= to && m.Date != kickoff &&
                           tier2Ids.Contains(m.LeagueId), ct);
    }

    private static DateTimeOffset SeasonStart(DateTimeOffset kickoff)
    {
        var year = kickoff.Month >= 7 ? kickoff.Year : kickoff.Year - 1;
        return new DateTimeOffset(year, 7, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
