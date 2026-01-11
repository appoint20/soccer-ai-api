using Mediator.Net.Context;
using Mediator.Net.Contracts;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.Matches.Queries;

public class GetUpcomingMatchesQueryHandler(
    IFixtureRepository fixtureRepository,
    ITeamStatsService teamStatsService,
    ITeamAnalyticsService teamAnalyticsService,
    IAdvancedStatsService advancedStatsService,
    IHistoricalDataRepository historicalRepository,
    ILeaguesRepository leaguesRepository)
    : IRequestHandler<GetUpcomingMatchesQuery, GetUpcomingMatchesResponse>
{
    public async Task<GetUpcomingMatchesResponse> Handle(
        IReceiveContext<GetUpcomingMatchesQuery> context, CancellationToken cancellationToken)
    {
        var query = context.Message;
        var fixtures = await fixtureRepository.GetFixturesAsync(query.Offset, query.Limit, cancellationToken);
        var total = await fixtureRepository.GetTotalCountAsync(cancellationToken);

        var allHistory = await historicalRepository.GetAllMatchesAsync();
        
        // Load Leagues for Name Mapping
        var leagues = await leaguesRepository.GetLeaguesAsync(cancellationToken);
        var leagueMap = leagues.ToDictionary(l => l.Id, l => l.Name, StringComparer.OrdinalIgnoreCase);

        // First pass: Calculate stats for all matches
        var enrichedMatches = new List<UpcomingMatchDto>();
        foreach (var match in fixtures)
        {
            // Calculate Generic Stats
            var homeStats = await teamStatsService.CalculateStatsAsync(match.HomeTeam, allHistory);
            var awayStats = await teamStatsService.CalculateStatsAsync(match.AwayTeam, allHistory);
            
            // Calculate Advanced Analytics
            var advancedAnalytics = await advancedStatsService.CalculateAnalyticsAsync(match.HomeTeam, match.AwayTeam, allHistory);

            // Calculate Traps && League Name
            var leagueName = leagueMap.TryGetValue(match.League, out var name) ? name : match.League;
            var matchWithLeague = match with { LeagueName = leagueName };

            // True H2H
            var h2hMatches = await historicalRepository.GetMatchesBetweenTeamsAsync(match.HomeTeam, match.AwayTeam);
            var h2hAnalysis = CalculateHistoricalH2H(h2hMatches, match.HomeTeam, match.AwayTeam);

            // --- New Analytics (Last 9 / Last 3) ---
            
            // Home Team Data
            var homeHistory = historicalRepository.GetMatchesForTeam(match.HomeTeam);
            var homeLast9 = homeHistory.OrderByDescending(m => m.Date).Take(9).ToList();
            var homeLast3Home = homeHistory
                .Where(m => string.Equals(m.HomeTeam, match.HomeTeam, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.Date)
                .Take(3)
                .ToList();

            var homeLast9Stats = teamAnalyticsService.CalculateStats(homeLast9, match.HomeTeam);
            var homeLast3HomeStats = teamAnalyticsService.CalculateStats(homeLast3Home, match.HomeTeam);

            // Away Team Data
            var awayHistory = historicalRepository.GetMatchesForTeam(match.AwayTeam);
            var awayLast9 = awayHistory.OrderByDescending(m => m.Date).Take(9).ToList();
            var awayLast3Away = awayHistory
                .Where(m => string.Equals(m.AwayTeam, match.AwayTeam, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.Date)
                .Take(3)
                .ToList();

            var awayLast9Stats = teamAnalyticsService.CalculateStats(awayLast9, match.AwayTeam);
            var awayLast3AwayStats = teamAnalyticsService.CalculateStats(awayLast3Away, match.AwayTeam);

            // Add to enriched list
            enrichedMatches.Add(matchWithLeague with 
            { 
                HomeTeamStats = homeStats,
                AwayTeamStats = awayStats,
                AdvancedAnalytics = advancedAnalytics,
                H2HAnalysis = h2hAnalysis,
                HomeLast9Overall = homeLast9Stats,
                AwayLast9Overall = awayLast9Stats,
                HomeLast3Home = homeLast3HomeStats,
                AwayLast3Away = awayLast3AwayStats
            });
        }
        
        return new GetUpcomingMatchesResponse
        {
            Data = new PagedResponse<UpcomingMatchDto>
            {
                Offset = query.Offset,
                Limit = query.Limit,
                Total = total,
                Items = enrichedMatches,
            }
        };
    }



    private H2HAnalysis CalculateHistoricalH2H(List<HistoricalMatchDto> matches, string homeTeam, string awayTeam)
    {
        // 1. Sort by Date Descending (Newest first) and Take 5
        var recentMatches = matches
            .OrderByDescending(m => m.Date)
            .Take(5)
            .ToList();

        // Calculate based on the "Last 5" subset
        int hWins = 0;
        int aWins = 0;
        int draws = 0;

        foreach (var m in recentMatches)
        {
            if (m.FTR == "D")
            {
                draws++;
                continue;
            }

            // Check who was home in that historical match and who won
            bool isHomeInHist = IsMatch(m.HomeTeam, homeTeam);
            if (isHomeInHist)
            {
                if (m.FTR == "H") hWins++;
                else if (m.FTR == "A") aWins++;
            }
            else // homeTeam was away in historical match
            {
                if (m.FTR == "A") hWins++; // They won away
                else if (m.FTR == "H") aWins++; // They lost (home won aka the other team)
            }
        }
        
        return new H2HAnalysis
        {
            HomeWinsLast5 = hWins,
            AwayWinsLast5 = aWins,
            DrawsLast5 = draws,
            Status = DetermineStatus(hWins, aWins, draws),
            AvgGoalsHome = recentMatches.Any() ? (double)recentMatches.Sum(m => IsMatch(m.HomeTeam, homeTeam) ? m.FTHG : m.FTAG) / recentMatches.Count : 0,
            AvgGoalsAway = recentMatches.Any() ? (double)recentMatches.Sum(m => IsMatch(m.AwayTeam, awayTeam) ? m.FTAG : m.FTHG) / recentMatches.Count : 0,
            FormHomeLast5 = GetH2HForm(recentMatches, homeTeam),
            FormAwayLast5 = GetH2HForm(recentMatches, awayTeam)
        };
    }

    private string GetH2HForm(List<HistoricalMatchDto> matches, string teamName)
    {
        // Calculate W/D/L string for this team against the opponent in these matches
        // matches are ordered Newest first.
        var form = "";
        
        // Take only last 5 matches for form string construction 
        // (Since matches list might be larger, e.g. 20)
        var recentMatches = matches.Take(5); 

        foreach (var m in recentMatches)
        {
            if (m.FTR == "D")
            {
                form += "D";
                continue;
            }
            
            var isHome = IsMatch(m.HomeTeam, teamName);
            if (isHome) form += (m.FTR == "H") ? "W" : "L";
            else form += (m.FTR == "A") ? "W" : "L";
        }
        
        // Return string. 
        // Note: This string is "Newest -> Oldest" (Left to Right) based on iteration. 
        // Typically form is shown Left=Recent.
        return form;
    }
    
    private bool IsMatch(string s1, string s2)
    {
        if (string.IsNullOrWhiteSpace(s1) || string.IsNullOrWhiteSpace(s2)) return false;
        
        // Fast paths
        if (string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase)) return true;
        if (s1.Contains(s2, StringComparison.OrdinalIgnoreCase) || s2.Contains(s1, StringComparison.OrdinalIgnoreCase)) return true;

        // Alias check (copied from service for now, should be shared ideally)
        if (AreAliases(s1, s2, "Man United", "Manchester United")) return true;
        if (AreAliases(s1, s2, "Man Utd", "Manchester United")) return true;
        if (AreAliases(s1, s2, "Man City", "Manchester City")) return true;
        if (AreAliases(s1, s2, "Wolves", "Wolverhampton Wanderers")) return true;
        if (AreAliases(s1, s2, "Spurs", "Tottenham Hotspur")) return true;
        if (AreAliases(s1, s2, "Nott'm Forest", "Nottingham Forest")) return true;
        if (AreAliases(s1, s2, "Sheff Utd", "Sheffield United")) return true;
        if (AreAliases(s1, s2, "West Ham", "West Ham United")) return true;
        if (AreAliases(s1, s2, "Newcastle", "Newcastle United")) return true;
        if (AreAliases(s1, s2, "Brighton", "Brighton & Hove Albion")) return true;

        return false;
    }

    private bool AreAliases(string actualA, string actualB, string alias1, string alias2)
    {
        return (string.Equals(actualA, alias1, StringComparison.OrdinalIgnoreCase) && string.Equals(actualB, alias2, StringComparison.OrdinalIgnoreCase)) ||
               (string.Equals(actualA, alias2, StringComparison.OrdinalIgnoreCase) && string.Equals(actualB, alias1, StringComparison.OrdinalIgnoreCase));
    }

    private string DetermineStatus(int homeWins, int awayWins, int draws)
    {
        // Require minimum 3 H2H matches to make determination
        int totalMatches = homeWins + awayWins + draws;
        if (totalMatches < 3) return "Insufficient Data";
        
        // Calculate win percentages
        double homeWinPct = (double)homeWins / totalMatches;
        double awayWinPct = (double)awayWins / totalMatches;
        double drawPct = (double)draws / totalMatches;
        
        // Thresholds for clear dominance
        const double DOMINANCE_THRESHOLD = 0.60; // 60%+ wins shows dominance
        const double MIN_WIN_DIFF = 2; // At least 2 more wins than opponent
        
        // Check for clear dominance patterns
        bool homeDominant = homeWinPct >= DOMINANCE_THRESHOLD && homeWins >= awayWins + MIN_WIN_DIFF;
        bool awayDominant = awayWinPct >= DOMINANCE_THRESHOLD && awayWins >= homeWins + MIN_WIN_DIFF;
        
        // Check for balanced/neutral patterns
        bool highDrawRate = drawPct >= 0.40; // 40%+ draws = unpredictable
        bool tooClose = Math.Abs(homeWins - awayWins) <= 1; // Within 1 win = balanced
        
        // Decision logic
        if (highDrawRate) return "Neutral (Draw-Heavy)";
        if (homeDominant) return "Home Advantage";
        if (awayDominant) return "Away Advantage";
        if (tooClose) return "Neutral (Balanced)";
        
        // Slight edge but not dominant - still neutral
        if (homeWins > awayWins) return "Neutral (Slight Home Edge)";
        if (awayWins > homeWins) return "Neutral (Slight Away Edge)";
        
        return "Neutral";
    }
}
