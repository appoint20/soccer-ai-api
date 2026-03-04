using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Infrastructure.MlNet.Models;

namespace SoccerAi.Infrastructure.MlNet;

public class MlTrainingDataBuilder(ILogger<MlTrainingDataBuilder> logger)
{
    private const int VenueWindow = 5;
    private const int OverallWindow = 7;
    private const int SeasonalWindow = 20;
    private const int H2hWindow = 5;
    private const int LeagueWindow = 100;

    public Task<List<MatchTrainingData>> BuildTrainingDataAsync(List<Fixture> allFinishedFixtures, CancellationToken ct = default)
    {
        logger.LogInformation("Starting fast in-memory ML feature extraction for {Count} fixtures...", allFinishedFixtures.Count);
        var sw = Stopwatch.StartNew();

        var trainingData = new List<MatchTrainingData>(allFinishedFixtures.Count);

        // State trackers
        var homeHistory = new Dictionary<int, Queue<Fixture>>();
        var awayHistory = new Dictionary<int, Queue<Fixture>>();
        var overallHistory = new Dictionary<int, List<Fixture>>();
        var h2hHistory = new Dictionary<string, Queue<Fixture>>();
        var leagueHistory = new Dictionary<int, Queue<Fixture>>();

        foreach (var f in allFinishedFixtures.OrderBy(f => f.Date))
        {
            // Initialize queues
            if (!homeHistory.ContainsKey(f.HomeTeamId)) homeHistory[f.HomeTeamId] = new Queue<Fixture>();
            if (!awayHistory.ContainsKey(f.AwayTeamId)) awayHistory[f.AwayTeamId] = new Queue<Fixture>();
            if (!overallHistory.ContainsKey(f.HomeTeamId)) overallHistory[f.HomeTeamId] = new List<Fixture>();
            if (!overallHistory.ContainsKey(f.AwayTeamId)) overallHistory[f.AwayTeamId] = new List<Fixture>();
            
            var h2hKey = f.HomeTeamId < f.AwayTeamId ? $"{f.HomeTeamId}_{f.AwayTeamId}" : $"{f.AwayTeamId}_{f.HomeTeamId}";
            if (!h2hHistory.ContainsKey(h2hKey)) h2hHistory[h2hKey] = new Queue<Fixture>();
            
            if (!leagueHistory.ContainsKey(f.LeagueId)) leagueHistory[f.LeagueId] = new Queue<Fixture>();

            // Current historical state BEFORE this match
            var homeV = homeHistory[f.HomeTeamId].ToList();
            var awayV = awayHistory[f.AwayTeamId].ToList();
            var h2h = h2hHistory[h2hKey].ToList();
            var league = leagueHistory[f.LeagueId].ToList();
            
            var homeO = overallHistory[f.HomeTeamId];
            var awayO = overallHistory[f.AwayTeamId];

            // Only generate training rows if we have at least SOME baseline history (e.g. 5 overall matches)
            // Python drops the first 100 rows globally; we can just require a local minimum.
            if (homeO.Count >= 5 && awayO.Count >= 5)
            {
                var homeRecent = homeO.TakeLast(OverallWindow).ToList();
                var awayRecent = awayO.TakeLast(OverallWindow).ToList();
                var homeSeasonal = homeO.TakeLast(SeasonalWindow).ToList();
                var awaySeasonal = awayO.TakeLast(SeasonalWindow).ToList();

                var features = BuildFeatures(f, homeV, awayV, h2h, league, homeRecent, awayRecent, homeSeasonal, awaySeasonal);

                // Targets
                int totalGoals = f.HomeGoal + f.AwayGoal;
                bool btts = f is { HomeGoal: > 0, AwayGoal: > 0 };
                bool over25 = totalGoals > 2.5;
                bool goals23 = totalGoals is 2 or 3;
                string result = f.HomeGoal > f.AwayGoal ? "Home" : (f.HomeGoal == f.AwayGoal ? "Draw" : "Away");

                trainingData.Add(new MatchTrainingData
                {
                    Features = features,
                    TargetBtts = btts,
                    TargetOver25 = over25,
                    TargetGoals23 = goals23,
                    TargetResult = result
                });
            }

            // --- Update State for NEXT matches ---
            homeHistory[f.HomeTeamId].Enqueue(f);
            if (homeHistory[f.HomeTeamId].Count > VenueWindow) homeHistory[f.HomeTeamId].Dequeue();

            awayHistory[f.AwayTeamId].Enqueue(f);
            if (awayHistory[f.AwayTeamId].Count > VenueWindow) awayHistory[f.AwayTeamId].Dequeue();

            overallHistory[f.HomeTeamId].Add(f);
            overallHistory[f.AwayTeamId].Add(f);
            // We can prune overall history to max seasonal window to save memory
            if (overallHistory[f.HomeTeamId].Count > SeasonalWindow * 2) 
                overallHistory[f.HomeTeamId] = overallHistory[f.HomeTeamId].TakeLast(SeasonalWindow).ToList();
            if (overallHistory[f.AwayTeamId].Count > SeasonalWindow * 2) 
                overallHistory[f.AwayTeamId] = overallHistory[f.AwayTeamId].TakeLast(SeasonalWindow).ToList();

            h2hHistory[h2hKey].Enqueue(f);
            if (h2hHistory[h2hKey].Count > H2hWindow) h2hHistory[h2hKey].Dequeue();

            leagueHistory[f.LeagueId].Enqueue(f);
            if (leagueHistory[f.LeagueId].Count > LeagueWindow) leagueHistory[f.LeagueId].Dequeue();
        }

        sw.Stop();
        logger.LogInformation("Built {Count} ML training rows in {Ms}ms", trainingData.Count, sw.ElapsedMilliseconds);
        return Task.FromResult(trainingData);
    }

    private static float[] BuildFeatures(
        Fixture fixture,
        List<Fixture> homeVenue, List<Fixture> awayVenue, List<Fixture> h2h, List<Fixture> league,
        List<Fixture> homeOverall, List<Fixture> awayOverall, List<Fixture> homeSeasonal, List<Fixture> awaySeasonal)
    {
        // ── Home venue stats ──
        var homeGoals = homeVenue.Select(f => f.HomeGoal).ToList();
        var homeConceded = homeVenue.Select(f => f.AwayGoal).ToList();
        var homeXg = homeVenue.Select(f => f.HomeXg).ToList();
        var homeShots = homeVenue.Select(f => f.HomeShots).ToList();
        var homeSot = homeVenue.Select(f => f.HomeShotsOnTarget).ToList();

        // ── Away venue stats ──
        var awayGoals = awayVenue.Select(f => f.AwayGoal).ToList();
        var awayConceded = awayVenue.Select(f => f.HomeGoal).ToList();
        var awayXg = awayVenue.Select(f => f.AwayXg).ToList();
        var awayShots = awayVenue.Select(f => f.AwayShots).ToList();
        var awaySot = awayVenue.Select(f => f.AwayShotsOnTarget).ToList();

        // ── Overall form — home team ──
        var homeOverallGoals = homeOverall.Select(f => GetTeamGoals(f, fixture.HomeTeamId)).ToList();
        var homeOverallConceded = homeOverall.Select(f => GetTeamConceded(f, fixture.HomeTeamId)).ToList();
        var homeOverallXg = homeOverall.Select(f => GetTeamXg(f, fixture.HomeTeamId)).ToList();

        // ── Overall form — away team ──
        var awayOverallGoals = awayOverall.Select(f => GetTeamGoals(f, fixture.AwayTeamId)).ToList();
        var awayOverallConceded = awayOverall.Select(f => GetTeamConceded(f, fixture.AwayTeamId)).ToList();
        var awayOverallXg = awayOverall.Select(f => GetTeamXg(f, fixture.AwayTeamId)).ToList();

        // Mean Reversion
        float homeSeasonalScoredAvg = SafeAvg(homeSeasonal.Select(f => GetTeamGoals(f, fixture.HomeTeamId)));
        float homeSeasonalXgAvg = SafeAvgDouble(homeSeasonal.Select(f => GetTeamXg(f, fixture.HomeTeamId)));
        float homeScoredDiff = SafeAvg(homeOverallGoals) - homeSeasonalScoredAvg;
        float homeXgDiff = SafeAvgDouble(homeOverallXg) - homeSeasonalXgAvg;

        float awaySeasonalScoredAvg = SafeAvg(awaySeasonal.Select(f => GetTeamGoals(f, fixture.AwayTeamId)));
        float awaySeasonalXgAvg = SafeAvgDouble(awaySeasonal.Select(f => GetTeamXg(f, fixture.AwayTeamId)));
        float awayScoredDiff = SafeAvg(awayOverallGoals) - awaySeasonalScoredAvg;
        float awayXgDiff = SafeAvgDouble(awayOverallXg) - awaySeasonalXgAvg;

        // Streaks
        float homeUnderStreak = CalculateStreak(EnumerateBackwards(homeSeasonal).ToList(), f => (f.HomeGoal + f.AwayGoal) < 2.5);
        float homeOverStreak = CalculateStreak(EnumerateBackwards(homeSeasonal).ToList(), f => (f.HomeGoal + f.AwayGoal) > 2.5);
        float homeBttsStreak = CalculateStreak(EnumerateBackwards(homeSeasonal).ToList(), f => f.HomeGoal > 0 && f.AwayGoal > 0);

        float awayUnderStreak = CalculateStreak(EnumerateBackwards(awaySeasonal).ToList(), f => (f.HomeGoal + f.AwayGoal) < 2.5);
        float awayOverStreak = CalculateStreak(EnumerateBackwards(awaySeasonal).ToList(), f => (f.HomeGoal + f.AwayGoal) > 2.5);
        float awayBttsStreak = CalculateStreak(EnumerateBackwards(awaySeasonal).ToList(), f => f.HomeGoal > 0 && f.AwayGoal > 0);

        var features = new float[]
        {
            SafeAvg(homeGoals), SafeAvg(homeConceded), SafeAvgDouble(homeXg), SafeAvg(homeShots), SafeAvg(homeSot),
            Rate(homeVenue.Count(f => f.HomeGoal > 0 && f.AwayGoal > 0), homeVenue.Count),
            Rate(homeVenue.Count(f => f.HomeGoal + f.AwayGoal > 2.5), homeVenue.Count),
            Rate(homeVenue.Count(f => f.AwayGoal == 0), homeVenue.Count),
            Rate(homeVenue.Count(f => f.HomeGoal == 0), homeVenue.Count),

            SafeAvg(homeOverallGoals), SafeAvg(homeOverallConceded), SafeAvgDouble(homeOverallXg),
            Rate(homeOverall.Count(f => f.HomeGoal > 0 && f.AwayGoal > 0), homeOverall.Count),
            Rate(homeOverall.Count(f => f.HomeGoal + f.AwayGoal > 2.5), homeOverall.Count),
            homeScoredDiff, homeXgDiff, homeUnderStreak, homeOverStreak, homeBttsStreak,

            SafeAvg(awayGoals), SafeAvg(awayConceded), SafeAvgDouble(awayXg), SafeAvg(awayShots), SafeAvg(awaySot),
            Rate(awayVenue.Count(f => f.HomeGoal > 0 && f.AwayGoal > 0), awayVenue.Count),
            Rate(awayVenue.Count(f => f.HomeGoal + f.AwayGoal > 2.5), awayVenue.Count),
            Rate(awayVenue.Count(f => f.HomeGoal == 0), awayVenue.Count),
            Rate(awayVenue.Count(f => f.AwayGoal == 0), awayVenue.Count),

            SafeAvg(awayOverallGoals), SafeAvg(awayOverallConceded), SafeAvgDouble(awayOverallXg),
            Rate(awayOverall.Count(f => f.HomeGoal > 0 && f.AwayGoal > 0), awayOverall.Count),
            Rate(awayOverall.Count(f => f.HomeGoal + f.AwayGoal > 2.5), awayOverall.Count),
            awayScoredDiff, awayXgDiff, awayUnderStreak, awayOverStreak, awayBttsStreak,

            h2h.Count > 0 ? SafeAvg(h2h.Select(f => f.HomeGoal + f.AwayGoal)) : 2.5f,
            h2h.Count > 0 ? Rate(h2h.Count(f => f.HomeGoal > 0 && f.AwayGoal > 0), h2h.Count) : 0.5f,
            h2h.Count > 0 ? Rate(h2h.Count(f => f.HomeGoal + f.AwayGoal > 2.5), h2h.Count) : 0.5f,

            SafeAvg(league.Select(l => l.HomeGoal + l.AwayGoal)),
            Rate(league.Count(l => l.HomeGoal > 0 && l.AwayGoal > 0), league.Count),
            Rate(league.Count(l => l.HomeGoal + l.AwayGoal > 2.5), league.Count),

            fixture.IsDerby ? 1.0f : 0.0f,
            fixture.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 1.0f : 0.0f,
            (float)fixture.Date.DayOfWeek,
            (float)fixture.Date.Month,
            (float)((fixture.Date.Month - 8 + 12) % 12),

            (float)(fixture.HomeElo ?? 1500.0), (float)(fixture.AwayElo ?? 1500.0),
            CalculateRestDays(fixture.Date, homeOverall),
            CalculateRestDays(fixture.Date, awayOverall),
            CalculateRestDays(fixture.Date, homeOverall) - CalculateRestDays(fixture.Date, awayOverall),
            15.0f, 60.0f, 0.0f, // Temp, Humidity, Turf (fake defaults like Python script)

            fixture.HomeWinOdds.HasValue && fixture.HomeWinOdds.Value > 0 ? (float)(1.0 / fixture.HomeWinOdds.Value) : 0f,
            fixture.DrawOdds.HasValue && fixture.DrawOdds.Value > 0 ? (float)(1.0 / fixture.DrawOdds.Value) : 0f,
            fixture.AwayWinOdds.HasValue && fixture.AwayWinOdds.Value > 0 ? (float)(1.0 / fixture.AwayWinOdds.Value) : 0f,
            fixture.Over25Odds.HasValue && fixture.Over25Odds.Value > 0 ? (float)(1.0 / fixture.Over25Odds.Value) : 0f,
            fixture.BttsYesOdds.HasValue && fixture.BttsYesOdds.Value > 0 ? (float)(1.0 / fixture.BttsYesOdds.Value) : 0f
        };

        return features;
    }

    private static IEnumerable<Fixture> EnumerateBackwards(List<Fixture> source)
    {
        for (int i = source.Count - 1; i >= 0; i--)
            yield return source[i];
    }

    private static float SafeAvg(IEnumerable<int> source)
    {
        var list = source.ToList();
        return list.Count > 0 ? (float)list.Average() : 2.5f;
    }

    private static float SafeAvgDouble(IEnumerable<double> source)
    {
        var list = source.ToList();
        return list.Count > 0 ? (float)list.Average() : 1.2f;
    }

    private static float Rate(int count, int total) => total > 0 ? (float)count / total : 0.5f;

    private static int GetTeamGoals(Fixture f, int teamId) => f.HomeTeamId == teamId ? f.HomeGoal : f.AwayGoal;
    private static int GetTeamConceded(Fixture f, int teamId) => f.HomeTeamId == teamId ? f.AwayGoal : f.HomeGoal;
    private static double GetTeamXg(Fixture f, int teamId) => f.HomeTeamId == teamId ? f.HomeXg : f.AwayXg;

    private static float CalculateStreak(List<Fixture> historyRecentFirst, Func<Fixture, bool> condition)
    {
        int streak = 0;
        foreach (var f in historyRecentFirst)
        {
            if (condition(f)) streak++;
            else break;
        }
        return streak;
    }

    private static float CalculateRestDays(DateTimeOffset currentDate, List<Fixture> history)
    {
        if (history.Count == 0) return 10f;
        var lastDate = history[^1].Date; // Last match is the ultimate element
        var days = (float)(currentDate - lastDate).TotalDays;
        return Math.Min(days, 14f);
    }
}
