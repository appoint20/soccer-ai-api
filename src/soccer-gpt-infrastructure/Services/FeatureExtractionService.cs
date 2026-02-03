using Microsoft.EntityFrameworkCore;
using soccer_gpt_application.Entities;
using soccer_gpt_application.Interfaces;

namespace soccer_gpt_infrastructure.Services;

public class FeatureExtractionService(IApplicationDbContext dbContext) : IFeatureExtractionService
{
    public async Task<float[]> BuildFeaturesAsync(Fixture fixture, CancellationToken ct)
    {
        // 1. Fetch Historical Data (Last 5 matches)
        // Python pipeline uses rolling window on HomeTeam's HOME matches and AwayTeam's AWAY matches
        
        var homeHistory = await dbContext.Fixtures
            .Where(f => f.HomeTeamId == fixture.HomeTeamId 
                   && f.Date < fixture.Date 
                   && f.Status == "FT")
            .OrderByDescending(f => f.Date)
            .Take(5)
            .ToListAsync(ct);

        var awayHistory = await dbContext.Fixtures
            .Where(f => f.AwayTeamId == fixture.AwayTeamId 
                   && f.Date < fixture.Date 
                   && f.Status == "FT")
            .OrderByDescending(f => f.Date)
            .Take(5)
            .ToListAsync(ct);

        // 2. Fetch H2H Data (Last 5 matches)
        var h2hHistory = await dbContext.Fixtures
            .Where(f => ((f.HomeTeamId == fixture.HomeTeamId && f.AwayTeamId == fixture.AwayTeamId) ||
                         (f.HomeTeamId == fixture.AwayTeamId && f.AwayTeamId == fixture.HomeTeamId))
                   && f.Date < fixture.Date
                   && f.Status == "FT")
            .OrderByDescending(f => f.Date)
            .Take(5)
            .ToListAsync(ct);

        // 3. Fetch League Data (Last 100 matches)
        // Optimization: Could be cached, but for now direct query
        var leagueHistory = await dbContext.Fixtures
            .Where(f => f.LeagueId == fixture.LeagueId 
                   && f.Date < fixture.Date 
                   && f.Status == "FT")
            .OrderByDescending(f => f.Date)
            .Take(100)
            .Select(f => new { f.HomeGoal, f.AwayGoal }) // Select only needed columns for performance
            .ToListAsync(ct);

        // Helper to safe average
        float SafeAvg(IEnumerable<int> source) => source.Any() ? (float)source.Average() : 0f;
        float SafeAvgDouble(IEnumerable<double> source) => source.Any() ? (float)source.Average() : 0f;
        float Rate(int count, int total) => total > 0 ? (float)count / total : 0f;

        // Calculate Home Features
        var homeGoals = homeHistory.Select(f => f.HomeGoal).ToList();
        var homeConceded = homeHistory.Select(f => f.AwayGoal).ToList();
        var homeXg = homeHistory.Select(f => f.HomeXg).ToList();
        var homeShots = homeHistory.Select(f => f.HomeShots).ToList();
        var homeSot = homeHistory.Select(f => f.HomeShotsOnTarget).ToList();
        
        // Calculate Away Features
        var awayGoals = awayHistory.Select(f => f.AwayGoal).ToList();
        var awayConceded = awayHistory.Select(f => f.HomeGoal).ToList();
        var awayXg = awayHistory.Select(f => f.AwayXg).ToList();
        var awayShots = awayHistory.Select(f => f.AwayShots).ToList();
        var awaySot = awayHistory.Select(f => f.AwayShotsOnTarget).ToList();

        // Calculate League Features
        var leagueTotalGoals = leagueHistory.Select(f => f.HomeGoal + f.AwayGoal).ToList();

        // 4. Construct Feature Array (30 features)
        return new float[]
        {
            // Home Team (at Home)
            SafeAvg(homeGoals),                                            // home_goals_scored_avg
            SafeAvg(homeConceded),                                         // home_goals_conceded_avg
            SafeAvgDouble(homeXg),                                         // home_xg_avg
            SafeAvg(homeShots),                                            // home_shots_avg
            SafeAvg(homeSot),                                              // home_shots_on_target_avg
            Rate(homeHistory.Count(f => f.HomeGoal > 0 && f.AwayGoal > 0), homeHistory.Count), // home_btts_rate
            Rate(homeHistory.Count(f => (f.HomeGoal + f.AwayGoal) > 2.5), homeHistory.Count),  // home_over25_rate
            Rate(homeHistory.Count(f => f.AwayGoal == 0), homeHistory.Count),                  // home_clean_sheet_rate
            Rate(homeHistory.Count(f => f.HomeGoal == 0), homeHistory.Count),                  // home_failed_to_score_rate

            // Away Team (at Away)
            SafeAvg(awayGoals),                                            // away_goals_scored_avg
            SafeAvg(awayConceded),                                         // away_goals_conceded_avg
            SafeAvgDouble(awayXg),                                         // away_xg_avg
            SafeAvg(awayShots),                                            // away_shots_avg
            SafeAvg(awaySot),                                              // away_shots_on_target_avg
            Rate(awayHistory.Count(f => f.HomeGoal > 0 && f.AwayGoal > 0), awayHistory.Count), // away_btts_rate
            Rate(awayHistory.Count(f => (f.HomeGoal + f.AwayGoal) > 2.5), awayHistory.Count),  // away_over25_rate
            Rate(awayHistory.Count(f => f.HomeGoal == 0), awayHistory.Count),                  // away_clean_sheet_rate
            Rate(awayHistory.Count(f => f.AwayGoal == 0), awayHistory.Count),                  // away_failed_to_score_rate

            // H2H
            SafeAvg(h2hHistory.Select(f => f.HomeGoal + f.AwayGoal)),      // h2h_total_goals_avg
            Rate(h2hHistory.Count(f => f.HomeGoal > 0 && f.AwayGoal > 0), h2hHistory.Count),   // h2h_btts_rate
            Rate(h2hHistory.Count(f => (f.HomeGoal + f.AwayGoal) > 2.5), h2hHistory.Count),    // h2h_over25_rate

            // League
            SafeAvg(leagueTotalGoals),                                     // league_avg_goals
            Rate(leagueHistory.Count(f => f.HomeGoal > 0 && f.AwayGoal > 0), leagueHistory.Count), // league_btts_rate
            Rate(leagueHistory.Count(f => (f.HomeGoal + f.AwayGoal) > 2.5), leagueHistory.Count),  // league_over25_rate

            // Other
            fixture.IsDerby ? 1.0f : 0.0f,                                 // is_derby
            
            // Odds-implied Probabilities
            fixture.HomeWinOdds.HasValue && fixture.HomeWinOdds > 0 ? (float)(1.0 / fixture.HomeWinOdds.Value) : 0f,
            fixture.DrawOdds.HasValue && fixture.DrawOdds > 0 ? (float)(1.0 / fixture.DrawOdds.Value) : 0f,
            fixture.AwayWinOdds.HasValue && fixture.AwayWinOdds > 0 ? (float)(1.0 / fixture.AwayWinOdds.Value) : 0f,
            fixture.Over25Odds.HasValue && fixture.Over25Odds > 0 ? (float)(1.0 / fixture.Over25Odds.Value) : 0f,
            fixture.BttsYesOdds.HasValue && fixture.BttsYesOdds > 0 ? (float)(1.0 / fixture.BttsYesOdds.Value) : 0f
        };
    }
}
