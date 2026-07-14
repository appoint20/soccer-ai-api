using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services;
using SoccerAi.Infrastructure.MlNet.Models;

namespace SoccerAi.Infrastructure.MlNet;

/// <summary>
/// Builds ML training rows: ONE row per fixture-market, generated strictly
/// from data available BEFORE the fixture date.
///
/// Features per row: Dixon-Coles probability, Shin-cleaned market probability,
/// Elo diff, rest days, recent form, league volatility.
///
/// Anti-leakage rules:
/// - Fixtures are processed chronologically; team history is updated only
///   AFTER a fixture's row has been generated.
/// - Dixon-Coles probabilities come from IDixonColesModel, whose SQL filter
///   is strictly Date &lt; fixture date.
/// - Odds are the fixture's own pre-match odds; Elo is the stored pre-match
///   snapshot.
/// </summary>
public class MlTrainingDataBuilder(ILogger<MlTrainingDataBuilder> logger)
{
    private const int FormWindow = 5;
    private const int MinHistoryMatches = 5;
    private const float DefaultRestDays = 10f;
    private const float MaxRestDays = 14f;
    private const float NeutralProbability = 0.5f;
    private const double DefaultElo = 1500.0;

    public async Task<List<MarketTrainingRow>> BuildAsync(
        List<Fixture> finishedFixtures,
        IDixonColesModel dixonColes,
        ILeagueVolatilityService volatility,
        CancellationToken ct = default)
    {
        logger.LogInformation("Building fixture-market training rows for {Count} fixtures...",
            finishedFixtures.Count);
        var sw = Stopwatch.StartNew();

        var rows = new List<MarketTrainingRow>(finishedFixtures.Count * MarketTrainingRow.Markets.All.Length);
        var teamHistory = new Dictionary<int, List<Fixture>>();
        var processed = 0;

        foreach (var f in finishedFixtures.OrderBy(f => f.Date))
        {
            ct.ThrowIfCancellationRequested();

            var homeHist = GetHistory(teamHistory, f.HomeTeamId);
            var awayHist = GetHistory(teamHistory, f.AwayTeamId);

            if (homeHist.Count >= MinHistoryMatches && awayHist.Count >= MinHistoryMatches)
            {
                // DC model applies its own strict Date < fixture.Date SQL filter.
                var dc = await dixonColes.CalculateProbabilitiesAsync(
                    f.LeagueId, f.HomeTeamId, f.AwayTeamId, f.Date, ct);

                if (dc != null)
                    AddFixtureRows(rows, f, dc, homeHist, awayHist, volatility);
            }

            // Update state only AFTER this fixture produced (or skipped) its rows.
            homeHist.Add(f);
            awayHist.Add(f);

            if (++processed % 1000 == 0)
                logger.LogInformation("...{Processed} fixtures processed, {Rows} rows so far",
                    processed, rows.Count);
        }

        sw.Stop();
        logger.LogInformation("Built {Rows} fixture-market rows in {Ms}ms", rows.Count, sw.ElapsedMilliseconds);
        return rows;
    }

    private static void AddFixtureRows(
        List<MarketTrainingRow> rows,
        Fixture f,
        PoissonProbabilities dc,
        List<Fixture> homeHist,
        List<Fixture> awayHist,
        ILeagueVolatilityService volatility)
    {
        // ── Shared pre-match features ──
        var eloDiff = (float)((f.HomeElo ?? DefaultElo) - (f.AwayElo ?? DefaultElo));
        var homeRest = RestDays(f.Date, homeHist);
        var awayRest = RestDays(f.Date, awayHist);
        var homeForm = Form(homeHist, f.HomeTeamId);
        var awayForm = Form(awayHist, f.AwayTeamId);
        var leagueVol = (float)volatility.GetVolatility(f.LeagueId);

        // ── Shin-cleaned market probabilities ──
        var market1X2 = MarketProbs1X2(f);
        var (marketOver, hasOver) = MarketProbOver25(f);
        var (marketBtts, hasBtts) = MarketProbBtts(f);

        // ── Labels ──
        var totalGoals = f.HomeGoal + f.AwayGoal;
        var labels = new Dictionary<string, bool>
        {
            [MarketTrainingRow.Markets.Over25] = totalGoals > 2,
            [MarketTrainingRow.Markets.Btts] = f is { HomeGoal: > 0, AwayGoal: > 0 },
            [MarketTrainingRow.Markets.Goals23] = totalGoals is 2 or 3,
            [MarketTrainingRow.Markets.HomeWin] = f.HomeGoal > f.AwayGoal,
            [MarketTrainingRow.Markets.AwayWin] = f.AwayGoal > f.HomeGoal
        };

        var dcProbs = new Dictionary<string, float>
        {
            [MarketTrainingRow.Markets.Over25] = (float)dc.Over25,
            [MarketTrainingRow.Markets.Btts] = (float)dc.BothTeamScoredGoal,
            [MarketTrainingRow.Markets.Goals23] = (float)dc.TwoToThreeGoals,
            [MarketTrainingRow.Markets.HomeWin] = (float)dc.HomeWin,
            [MarketTrainingRow.Markets.AwayWin] = (float)dc.AwayWin
        };

        var marketProbs = new Dictionary<string, (float P, bool Has)>
        {
            [MarketTrainingRow.Markets.Over25] = (marketOver, hasOver),
            [MarketTrainingRow.Markets.Btts] = (marketBtts, hasBtts),
            [MarketTrainingRow.Markets.Goals23] = (NeutralProbability, false), // no odds market exists
            [MarketTrainingRow.Markets.HomeWin] = market1X2.HasValue
                ? ((float)market1X2.Value.Home, true) : (NeutralProbability, false),
            [MarketTrainingRow.Markets.AwayWin] = market1X2.HasValue
                ? ((float)market1X2.Value.Away, true) : (NeutralProbability, false)
        };

        foreach (var market in MarketTrainingRow.Markets.All)
        {
            var (marketP, hasMarketP) = marketProbs[market];
            rows.Add(new MarketTrainingRow
            {
                FixtureId = f.Id,
                LeagueId = f.LeagueId,
                Date = f.Date.UtcDateTime,
                Market = market,

                DcProb = dcProbs[market],
                MarketProb = marketP,
                HasMarketProb = hasMarketP ? 1f : 0f,
                DcMarketDelta = dcProbs[market] - marketP,
                EloDiff = eloDiff,
                HomeRestDays = homeRest,
                AwayRestDays = awayRest,
                RestDaysDiff = homeRest - awayRest,
                HomeForm = homeForm,
                AwayForm = awayForm,
                FormDiff = homeForm - awayForm,
                LeagueVolatility = leagueVol,

                Label = labels[market]
            });
        }
    }

    // ── Market probability helpers (Shin-margin-removed) ────────────────────

    private static (double Home, double Away)? MarketProbs1X2(Fixture f)
    {
        if (!IsValid(f.HomeWinOdds) || !IsValid(f.DrawOdds) || !IsValid(f.AwayWinOdds))
            return null;

        var probs = ShinMarginRemoval.TrueProbabilities(
            [f.HomeWinOdds!.Value, f.DrawOdds!.Value, f.AwayWinOdds!.Value]);
        return (probs[0], probs[2]);
    }

    private static (float P, bool Has) MarketProbOver25(Fixture f)
    {
        if (IsValid(f.Over25Odds) && IsValid(f.Under25Odds))
            return ((float)ShinMarginRemoval.TrueProbability(f.Over25Odds!.Value, f.Under25Odds!.Value), true);
        if (IsValid(f.Over25Odds))
            return ((float)(1.0 / f.Over25Odds!.Value), true);
        return (NeutralProbability, false);
    }

    private static (float P, bool Has) MarketProbBtts(Fixture f) =>
        IsValid(f.BttsYesOdds)
            ? ((float)(1.0 / f.BttsYesOdds!.Value), true)
            : (NeutralProbability, false);

    private static bool IsValid(double? odds) => odds is > 1.0;

    // ── History features ─────────────────────────────────────────────────────

    private static List<Fixture> GetHistory(Dictionary<int, List<Fixture>> store, int teamId)
    {
        if (!store.TryGetValue(teamId, out var list))
            store[teamId] = list = [];
        return list;
    }

    private static float RestDays(DateTimeOffset date, List<Fixture> history)
    {
        if (history.Count == 0) return DefaultRestDays;
        var days = (float)(date - history[^1].Date).TotalDays;
        return Math.Clamp(days, 0f, MaxRestDays);
    }

    /// <summary>Points share over the last FormWindow matches: won points / possible points.</summary>
    private static float Form(List<Fixture> history, int teamId)
    {
        var recent = history.TakeLast(FormWindow).ToList();
        if (recent.Count == 0) return 0.5f;

        var points = recent.Sum(f =>
        {
            var scored = f.HomeTeamId == teamId ? f.HomeGoal : f.AwayGoal;
            var conceded = f.HomeTeamId == teamId ? f.AwayGoal : f.HomeGoal;
            return scored > conceded ? 3 : scored == conceded ? 1 : 0;
        });

        return points / (recent.Count * 3f);
    }
}
