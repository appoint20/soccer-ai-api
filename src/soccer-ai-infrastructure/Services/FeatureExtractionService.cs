using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// Extracts a 54-feature vector from historical match data for ML predictions.
/// Follows clean architecture: data fetching is separated from pure calculation logic.
/// </summary>
/// <remarks>
/// Feature vector schema must match <c>scripts/ml/models/feature_columns.json</c> exactly.
/// Any changes to feature count or order require model retraining.
/// </remarks>
public sealed class FeatureExtractionService(
    IApplicationDbContext dbContext,
    ILogger<FeatureExtractionService> logger) : IFeatureExtractionService
{
    // ──────────────────────────── Configuration ────────────────────────────

    private static class FeatureConfig
    {
        // Query window sizes
        public const int VenueMatchWindow     = 5;   // Last N home-only or away-only matches
        public const int OverallMatchWindow    = 7;   // Last N matches regardless of venue
        public const int SeasonalMatchWindow   = 20;  // Expanding window for mean reversion
        public const int H2HMatchWindow        = 5;   // Head-to-head history depth
        public const int LeagueMatchWindow     = 100; // League-wide rolling average

        // Domain-informed defaults (used when history is empty)
        public const float DefaultGoalsAvg     = 2.5f;
        public const float DefaultBttsRate     = 0.5f;
        public const float DefaultOver25Rate   = 0.5f;

        // Expected output
        public const int ExpectedFeatureCount  = 62;
    }

    // ──────────────────────────── Internal DTOs ────────────────────────────
    
    private sealed record TeamElo(double Home, double Away);

    /// <summary>All historical data needed to build one feature vector.</summary>
    private sealed record HistoricalData(
        List<Fixture> HomeVenue,      // Home team's last N HOME matches
        List<Fixture> AwayVenue,      // Away team's last N AWAY matches
        List<Fixture> H2H,           // Head-to-head encounters
        List<LeagueGoals> League,    // League-wide goal stats
        List<Fixture> HomeOverall,   // Home team's last N matches (any venue)
        List<Fixture> AwayOverall,   // Away team's last N matches (any venue)
        List<Fixture> HomeSeasonal,  // Home team's expanded window (mean reversion)
        List<Fixture> AwaySeasonal,  // Away team's expanded window (mean reversion)
        TeamElo Elo);                // Current ELOs for the teams

    /// <summary>Minimal projection for league queries.</summary>
    private sealed record LeagueGoals(int HomeGoal, int AwayGoal);

    // ──────────────────────────── Public API ───────────────────────────────

    public async Task<float[]> BuildFeaturesAsync(Fixture fixture, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        if (fixture.HomeTeamId <= 0 || fixture.AwayTeamId <= 0)
            throw new ArgumentException("Invalid team IDs", nameof(fixture));

        if (fixture.LeagueId <= 0)
            throw new ArgumentException("Invalid league ID", nameof(fixture));

        if (fixture.Date == default)
            throw new ArgumentException("Invalid fixture date", nameof(fixture));

        var sw = Stopwatch.StartNew();

        var data = await FetchAllHistoricalDataAsync(fixture, ct);
        var features = BuildFeatureVector(fixture, data);

        sw.Stop();
        logger.LogInformation(
            "Generated {Count} features for {Home} vs {Away} (fixture {Id}) in {Ms}ms",
            features.Length, fixture.HomeTeamId, fixture.AwayTeamId, fixture.Id, sw.ElapsedMilliseconds);

        return features;
    }

    // ──────────────────────────── Data Fetching ────────────────────────────

    /// <summary>
    /// Executes all 8 historical queries in parallel with <c>AsNoTracking</c>.
    /// </summary>
    private async Task<HistoricalData> FetchAllHistoricalDataAsync(Fixture fixture, CancellationToken ct)
    {
        var homeVenue    = await FetchVenueHistoryAsync(fixture.HomeTeamId, fixture.Date, isHome: true, ct);
        var awayVenue    = await FetchVenueHistoryAsync(fixture.AwayTeamId, fixture.Date, isHome: false, ct);
        var h2h          = await FetchH2HHistoryAsync(fixture.HomeTeamId, fixture.AwayTeamId, fixture.Date, ct);
        var league       = await FetchLeagueHistoryAsync(fixture.LeagueId, fixture.Date, ct);
        var homeOverall  = await FetchOverallHistoryAsync(fixture.HomeTeamId, fixture.Date, FeatureConfig.OverallMatchWindow, ct);
        var awayOverall  = await FetchOverallHistoryAsync(fixture.AwayTeamId, fixture.Date, FeatureConfig.OverallMatchWindow, ct);
        var homeSeasonal = await FetchOverallHistoryAsync(fixture.HomeTeamId, fixture.Date, FeatureConfig.SeasonalMatchWindow, ct);
        var awaySeasonal = await FetchOverallHistoryAsync(fixture.AwayTeamId, fixture.Date, FeatureConfig.SeasonalMatchWindow, ct);
        var elo          = await FetchCurrentEloAsync(fixture.HomeTeamId, fixture.AwayTeamId, ct);

        return new HistoricalData(
            HomeVenue:    homeVenue,
            AwayVenue:    awayVenue,
            H2H:         h2h,
            League:       league,
            HomeOverall:  homeOverall,
            AwayOverall:  awayOverall,
            HomeSeasonal: homeSeasonal,
            AwaySeasonal: awaySeasonal,
            Elo:          elo);
    }

    private async Task<TeamElo> FetchCurrentEloAsync(int homeTeamId, int awayTeamId, CancellationToken ct)
    {
        var teamIds = new[] { homeTeamId, awayTeamId };
        var elos = await dbContext.Teams.AsNoTracking()
            .Where(t => teamIds.Contains(t.ApiId))
            .Select(t => new { t.ApiId, t.Elo })
            .ToListAsync(ct);

        var homeElo = elos.FirstOrDefault(t => t.ApiId == homeTeamId)?.Elo ?? 1500.0;
        var awayElo = elos.FirstOrDefault(t => t.ApiId == awayTeamId)?.Elo ?? 1500.0;

        return new TeamElo(homeElo, awayElo);
    }

    private async Task<List<Fixture>> FetchVenueHistoryAsync(
        int teamId, DateTimeOffset before, bool isHome, CancellationToken ct)
    {
        var query = dbContext.Fixtures.AsNoTracking()
            .Where(f => f.Status == "FT");

        query = isHome
            ? query.Where(f => f.HomeTeamId == teamId)
            : query.Where(f => f.AwayTeamId == teamId);

        var matches = await query.ToListAsync(ct);

        return matches
            .Where(f => f.Date < before)
            .OrderByDescending(f => f.Date)
            .Take(FeatureConfig.VenueMatchWindow)
            .ToList();
    }

    private async Task<List<Fixture>> FetchH2HHistoryAsync(
        int homeTeamId, int awayTeamId, DateTimeOffset before, CancellationToken ct)
    {
        var matches = await dbContext.Fixtures.AsNoTracking()
            .Where(f => ((f.HomeTeamId == homeTeamId && f.AwayTeamId == awayTeamId) ||
                         (f.HomeTeamId == awayTeamId && f.AwayTeamId == homeTeamId))
                        && f.Status == "FT")
            .ToListAsync(ct);

        return matches
            .Where(f => f.Date < before)
            .OrderByDescending(f => f.Date)
            .Take(FeatureConfig.H2HMatchWindow)
            .ToList();
    }

    private async Task<List<LeagueGoals>> FetchLeagueHistoryAsync(
        int leagueId, DateTimeOffset before, CancellationToken ct)
    {
        // League history can be large, but for SQLite we must filter dates in-memory.
        // We limit to the same league and finished status.
        var matches = await dbContext.Fixtures.AsNoTracking()
            .Where(f => f.LeagueId == leagueId && f.Status == "FT")
            .Select(f => new { f.Date, f.HomeGoal, f.AwayGoal })
            .ToListAsync(ct);

        return matches
            .Where(f => f.Date < before)
            .OrderByDescending(f => f.Date)
            .Take(FeatureConfig.LeagueMatchWindow)
            .Select(f => new LeagueGoals(f.HomeGoal, f.AwayGoal))
            .ToList();
    }

    private async Task<List<Fixture>> FetchOverallHistoryAsync(
        int teamId, DateTimeOffset before, int count, CancellationToken ct)
    {
        var matches = await dbContext.Fixtures.AsNoTracking()
            .Where(f => (f.HomeTeamId == teamId || f.AwayTeamId == teamId)
                        && f.Status == "FT")
            .ToListAsync(ct);

        return matches
            .Where(f => f.Date < before)
            .OrderByDescending(f => f.Date)
            .Take(count)
            .ToList();
    }

    // ──────────────────────────── Feature Vector ──────────────────────────

    /// <summary>
    /// Pure calculation — no I/O, fully unit-testable.
    /// Returns a 54-element float array matching <c>feature_columns.json</c>.
    /// </summary>
    private static float[] BuildFeatureVector(Fixture fixture, HistoricalData data)
    {
        // ── Home venue stats ──
        var homeGoals    = data.HomeVenue.Select(f => f.HomeGoal).ToList();
        var homeConceded = data.HomeVenue.Select(f => f.AwayGoal).ToList();
        var homeXg       = data.HomeVenue.Select(f => f.HomeXg).ToList();
        var homeShots    = data.HomeVenue.Select(f => f.HomeShots).ToList();
        var homeSot      = data.HomeVenue.Select(f => f.HomeShotsOnTarget).ToList();

        // ── Away venue stats ──
        var awayGoals    = data.AwayVenue.Select(f => f.AwayGoal).ToList();
        var awayConceded = data.AwayVenue.Select(f => f.HomeGoal).ToList();
        var awayXg       = data.AwayVenue.Select(f => f.AwayXg).ToList();
        var awayShots    = data.AwayVenue.Select(f => f.AwayShots).ToList();
        var awaySot      = data.AwayVenue.Select(f => f.AwayShotsOnTarget).ToList();

        // ── League stats ──
        var leagueTotalGoals = data.League.Select(l => l.HomeGoal + l.AwayGoal).ToList();

        // ── Overall form — home team (last 7 matches, any venue) ──
        var homeOverallGoals    = data.HomeOverall.Select(f => GetTeamGoals(f, fixture.HomeTeamId)).ToList();
        var homeOverallConceded = data.HomeOverall.Select(f => GetTeamConceded(f, fixture.HomeTeamId)).ToList();
        var homeOverallXg       = data.HomeOverall.Select(f => GetTeamXg(f, fixture.HomeTeamId)).ToList();

        // ── Overall form — away team (last 7 matches, any venue) ──
        var awayOverallGoals    = data.AwayOverall.Select(f => GetTeamGoals(f, fixture.AwayTeamId)).ToList();
        var awayOverallConceded = data.AwayOverall.Select(f => GetTeamConceded(f, fixture.AwayTeamId)).ToList();
        var awayOverallXg       = data.AwayOverall.Select(f => GetTeamXg(f, fixture.AwayTeamId)).ToList();

        // ── Mean reversion (recent 7 vs seasonal 20) ──
        float homeSeasonalScoredAvg = SafeAvg(data.HomeSeasonal.Select(f => GetTeamGoals(f, fixture.HomeTeamId)));
        float homeSeasonalXgAvg     = SafeAvgDouble(data.HomeSeasonal.Select(f => GetTeamXg(f, fixture.HomeTeamId)));
        float homeScoredDiff        = SafeAvg(homeOverallGoals) - homeSeasonalScoredAvg;
        float homeXgDiff            = SafeAvgDouble(homeOverallXg) - homeSeasonalXgAvg;

        float awaySeasonalScoredAvg = SafeAvg(data.AwaySeasonal.Select(f => GetTeamGoals(f, fixture.AwayTeamId)));
        float awaySeasonalXgAvg     = SafeAvgDouble(data.AwaySeasonal.Select(f => GetTeamXg(f, fixture.AwayTeamId)));
        float awayScoredDiff        = SafeAvg(awayOverallGoals) - awaySeasonalScoredAvg;
        float awayXgDiff            = SafeAvgDouble(awayOverallXg) - awaySeasonalXgAvg;

        // ── Streaks (computed over seasonal window) ──
        float homeUnderStreak = CalculateStreak(data.HomeSeasonal, f => (f.HomeGoal + f.AwayGoal) < 2.5);
        float homeOverStreak  = CalculateStreak(data.HomeSeasonal, f => (f.HomeGoal + f.AwayGoal) > 2.5);
        float homeBttsStreak  = CalculateStreak(data.HomeSeasonal, f => f.HomeGoal > 0 && f.AwayGoal > 0);

        float awayUnderStreak = CalculateStreak(data.AwaySeasonal, f => (f.HomeGoal + f.AwayGoal) < 2.5);
        float awayOverStreak  = CalculateStreak(data.AwaySeasonal, f => (f.HomeGoal + f.AwayGoal) > 2.5);
        float awayBttsStreak  = CalculateStreak(data.AwaySeasonal, f => f.HomeGoal > 0 && f.AwayGoal > 0);

        // ── Assemble 54-feature vector ──
        var features = new float[]
        {
            // ═══════ HOME TEAM — VENUE FEATURES (9) ═══════
            SafeAvg(homeGoals),                                                                        // [ 0] home_goals_scored_avg
            SafeAvg(homeConceded),                                                                     // [ 1] home_goals_conceded_avg
            SafeAvgDouble(homeXg),                                                                     // [ 2] home_xg_avg
            SafeAvg(homeShots),                                                                        // [ 3] home_shots_avg
            SafeAvg(homeSot),                                                                          // [ 4] home_shots_on_target_avg
            Rate(data.HomeVenue.Count(f => f.HomeGoal > 0 && f.AwayGoal > 0), data.HomeVenue.Count),   // [ 5] home_btts_rate
            Rate(data.HomeVenue.Count(f => f.HomeGoal + f.AwayGoal > 2.5), data.HomeVenue.Count),      // [ 6] home_over25_rate
            Rate(data.HomeVenue.Count(f => f.AwayGoal == 0), data.HomeVenue.Count),                    // [ 7] home_clean_sheet_rate
            Rate(data.HomeVenue.Count(f => f.HomeGoal == 0), data.HomeVenue.Count),                    // [ 8] home_failed_to_score_rate

            // ═══════ HOME TEAM — OVERALL FORM + STREAKS (10) ═══════
            SafeAvg(homeOverallGoals),                                                                     // [ 9] home_overall_goals_scored_avg
            SafeAvg(homeOverallConceded),                                                                  // [10] home_overall_goals_conceded_avg
            SafeAvgDouble(homeOverallXg),                                                                  // [11] home_overall_xg_avg
            Rate(data.HomeOverall.Count(f => f.HomeGoal > 0 && f.AwayGoal > 0), data.HomeOverall.Count),   // [12] home_overall_btts_rate
            Rate(data.HomeOverall.Count(f => f.HomeGoal + f.AwayGoal > 2.5), data.HomeOverall.Count),      // [13] home_overall_over25_rate
            homeScoredDiff,                                                                                // [14] home_overall_scored_diff
            homeXgDiff,                                                                                    // [15] home_overall_xg_diff
            homeUnderStreak,                                                                               // [16] home_overall_under_streak
            homeOverStreak,                                                                                // [17] home_overall_over_streak
            homeBttsStreak,                                                                                // [18] home_overall_btts_streak

            // ═══════ AWAY TEAM — VENUE FEATURES (9) ═══════
            SafeAvg(awayGoals),                                                                        // [19] away_goals_scored_avg
            SafeAvg(awayConceded),                                                                     // [20] away_goals_conceded_avg
            SafeAvgDouble(awayXg),                                                                     // [21] away_xg_avg
            SafeAvg(awayShots),                                                                        // [22] away_shots_avg
            SafeAvg(awaySot),                                                                          // [23] away_shots_on_target_avg
            Rate(data.AwayVenue.Count(f => f.HomeGoal > 0 && f.AwayGoal > 0), data.AwayVenue.Count),   // [24] away_btts_rate
            Rate(data.AwayVenue.Count(f => f.HomeGoal + f.AwayGoal > 2.5), data.AwayVenue.Count),      // [25] away_over25_rate
            Rate(data.AwayVenue.Count(f => f.HomeGoal == 0), data.AwayVenue.Count),                    // [26] away_clean_sheet_rate  ← FIXED: was f.AwayGoal==0
            Rate(data.AwayVenue.Count(f => f.AwayGoal == 0), data.AwayVenue.Count),                    // [27] away_failed_to_score_rate

            // ═══════ AWAY TEAM — OVERALL FORM + STREAKS (10) ═══════
            SafeAvg(awayOverallGoals),                                                                     // [28] away_overall_goals_scored_avg
            SafeAvg(awayOverallConceded),                                                                  // [29] away_overall_goals_conceded_avg
            SafeAvgDouble(awayOverallXg),                                                                  // [30] away_overall_xg_avg
            Rate(data.AwayOverall.Count(f => f.HomeGoal > 0 && f.AwayGoal > 0), data.AwayOverall.Count),   // [31] away_overall_btts_rate
            Rate(data.AwayOverall.Count(f => f.HomeGoal + f.AwayGoal > 2.5), data.AwayOverall.Count),      // [32] away_overall_over25_rate
            awayScoredDiff,                                                                                // [33] away_overall_scored_diff
            awayXgDiff,                                                                                    // [34] away_overall_xg_diff
            awayUnderStreak,                                                                               // [35] away_overall_under_streak
            awayOverStreak,                                                                                // [36] away_overall_over_streak
            awayBttsStreak,                                                                                // [37] away_overall_btts_streak

            // ═══════ HEAD-TO-HEAD (3) ═══════
            data.H2H.Count > 0
                ? SafeAvg(data.H2H.Select(f => f.HomeGoal + f.AwayGoal))
                : FeatureConfig.DefaultGoalsAvg,                                                           // [38] h2h_total_goals_avg
            data.H2H.Count > 0
                ? Rate(data.H2H.Count(f => f.HomeGoal > 0 && f.AwayGoal > 0), data.H2H.Count)
                : FeatureConfig.DefaultBttsRate,                                                           // [39] h2h_btts_rate
            data.H2H.Count > 0
                ? Rate(data.H2H.Count(f => f.HomeGoal + f.AwayGoal > 2.5), data.H2H.Count)
                : FeatureConfig.DefaultOver25Rate,                                                         // [40] h2h_over25_rate

            // ═══════ LEAGUE (3) ═══════
            SafeAvg(leagueTotalGoals),                                                                     // [41] league_avg_goals
            Rate(data.League.Count(l => l.HomeGoal > 0 && l.AwayGoal > 0), data.League.Count),             // [42] league_btts_rate
            Rate(data.League.Count(l => l.HomeGoal + l.AwayGoal > 2.5), data.League.Count),                // [43] league_over25_rate

            // ═══════ FLAGS (1) ═══════
            fixture.IsDerby ? 1.0f : 0.0f,                                                                // [44] is_derby

            // ═══════ TEMPORAL & SEASONALITY (4) ═══════
            IsWeekend(fixture.Date) ? 1.0f : 0.0f,                                                        // [45] is_weekend
            (float)fixture.Date.DayOfWeek,                                                                 // [46] day_of_week
            (float)fixture.Date.Month,                                                                     // [47] month
            (float)((fixture.Date.Month - 8 + 12) % 12),                                                  // [48] season_month_idx

            // ═══════ NEW INJECTIONS (8) ═══════
            (float)data.Elo.Home,                                                                         // [49] home_elo
            (float)data.Elo.Away,                                                                         // [50] away_elo
            CalculateRestDays(fixture.Date, data.HomeOverall),                                            // [51] home_rest_days
            CalculateRestDays(fixture.Date, data.AwayOverall),                                            // [52] away_rest_days
            CalculateRestDays(fixture.Date, data.HomeOverall) - CalculateRestDays(fixture.Date, data.AwayOverall), // [53] rest_diff
            15.0f,                                                                                        // [54] temp
            60.0f,                                                                                        // [55] humidity
            0.0f,                                                                                         // [56] is_artificial_turf

            // ═══════ ODDS-IMPLIED PROBABILITIES (5) ═══════
            ImpliedProbability(fixture.HomeWinOdds),                                                       // [57] home_win_implied_prob
            ImpliedProbability(fixture.DrawOdds),                                                          // [58] draw_implied_prob
            ImpliedProbability(fixture.AwayWinOdds),                                                       // [59] away_win_implied_prob
            ImpliedProbability(fixture.Over25Odds),                                                        // [60] over25_implied_prob
            ImpliedProbability(fixture.BttsYesOdds),                                                       // [61] btts_yes_odds
        };

        Debug.Assert(features.Length == FeatureConfig.ExpectedFeatureCount,
            $"Feature count mismatch: expected {FeatureConfig.ExpectedFeatureCount}, got {features.Length}");

        return features;
    }

    // ──────────────────────────── Helper Methods ──────────────────────────

    private static float SafeAvg(IEnumerable<int> source)
    {
        var list = source as IList<int> ?? source.ToList();
        return list.Count > 0 ? (float)list.Average() : 0f;
    }

    private static float SafeAvgDouble(IEnumerable<double> source)
    {
        var list = source as IList<double> ?? source.ToList();
        return list.Count > 0 ? (float)list.Average() : 0f;
    }

    private static float Rate(int count, int total)
        => total > 0 ? (float)count / total : 0f;

    private static int GetTeamGoals(Fixture f, int teamId)
        => f.HomeTeamId == teamId ? f.HomeGoal : f.AwayGoal;

    private static int GetTeamConceded(Fixture f, int teamId)
        => f.HomeTeamId == teamId ? f.AwayGoal : f.HomeGoal;

    private static double GetTeamXg(Fixture f, int teamId)
        => f.HomeTeamId == teamId ? f.HomeXg : f.AwayXg;

    private static float CalculateStreak(IEnumerable<Fixture> history, Func<Fixture, bool> condition)
    {
        int streak = 0;
        foreach (var f in history)
        {
            if (condition(f)) streak++;
            else break;
        }
        return streak;
    }

    private static bool IsWeekend(DateTimeOffset date)
        => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static float ImpliedProbability(double? odds)
        => odds is > 0 ? (float)(1.0 / odds.Value) : 0f;

    private static float CalculateRestDays(DateTimeOffset currentDate, List<Fixture> history)
    {
        if (history == null || history.Count == 0) return 10f;
        var lastDate = history[0].Date;
        var days = (float)(currentDate - lastDate).TotalDays;
        return Math.Min(days, 14f);
    }

    private static bool IsArtificialTurf(string? surface)
        => surface != null && (surface.Contains("turf", StringComparison.OrdinalIgnoreCase) || 
                               surface.Contains("artificial", StringComparison.OrdinalIgnoreCase));
}
