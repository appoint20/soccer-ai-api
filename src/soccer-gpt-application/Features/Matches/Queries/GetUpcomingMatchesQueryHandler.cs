
using Mediator.Net.Context;
using Mediator.Net.Contracts;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models;

namespace soccer_gpt_application.Features.Matches.Queries;

public class GetUpcomingMatchesQuery : IRequest
{
    public int Offset { get; set; } = 0;
    public int Limit { get; set; } = 10;
}

public class GetUpcomingMatchesResponse : IResponse
{
    public PagedResponse<UpcomingMatchDto> Data { get; set; } = new();
}

public class GetUpcomingMatchesQueryHandler(
    IFixtureRepository fixtureRepository,
    ITeamStatsService teamStatsService,
    IAdvancedStatsService advancedStatsService,
    IHistoricalDataRepository historicalRepository,
    ILeaguesRepository leaguesRepository,
    ITrapDetectionService trapDetectionService,
    IMlPredictionService mlPredictionService,
    IGeminiAnalysisService geminiAnalysisService)
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
            // Calculate Rich Stats
            var homeStats = await teamStatsService.CalculateStatsAsync(match.HomeTeam, allHistory);
            var awayStats = await teamStatsService.CalculateStatsAsync(match.AwayTeam, allHistory);
            
            // Calculate Advanced Analytics
            var advancedAnalytics = await advancedStatsService.CalculateAnalyticsAsync(match.HomeTeam, match.AwayTeam, allHistory);

            // Calculate Traps
            var leagueName = leagueMap.TryGetValue(match.League, out var name) ? name : match.League;
            var matchWithLeague = match with { LeagueName = leagueName };
            var traps = trapDetectionService.AnalyzeTraps(matchWithLeague, advancedAnalytics);

            // ML Prediction
            var mlPred = await mlPredictionService.PredictMatchAsync(matchWithLeague, allHistory);

            // True H2H
            var h2hMatches = await historicalRepository.GetMatchesBetweenTeamsAsync(match.HomeTeam, match.AwayTeam);
            var h2hAnalysis = CalculateHistoricalH2H(h2hMatches, match.HomeTeam, match.AwayTeam);

            // Add to enriched list (without Gemini yet)
            enrichedMatches.Add(matchWithLeague with 
            { 
                HomeTeamStats = homeStats,
                AwayTeamStats = awayStats,
                AdvancedAnalytics = advancedAnalytics,
                H2HAnalysis = h2hAnalysis,
                Traps = traps,
                MlPrediction = mlPred
            });
        }
        
        // Second pass: Batch Gemini calls per league
        var geminiAnalyses = new Dictionary<string, GeminiMatchAnalysis>();
        var leagueGroups = enrichedMatches.GroupBy(m => m.LeagueName);
        
        foreach (var leagueGroup in leagueGroups)
        {
            var leagueAnalyses = await geminiAnalysisService.AnalyzeMatchBatchAsync(
                leagueGroup.Key, 
                leagueGroup.ToList(), 
                cancellationToken);
            
            foreach (var (key, analysis) in leagueAnalyses)
            {
                geminiAnalyses[key] = analysis;
            }
        }
        
        // Third pass: Add Gemini analysis to matches
        var finalMatches = enrichedMatches.Select(match =>
        {
            var key = $"{match.HomeTeam}-{match.AwayTeam}";
            var geminiAnalysis = geminiAnalyses.GetValueOrDefault(key);
            
            return match with
            {
                Gemini = geminiAnalysis != null ? new GeminiAnalysisDto
                {
                    Analysis = geminiAnalysis.Analysis,
                    Prediction = geminiAnalysis.Prediction,
                    ConfidenceLevel = geminiAnalysis.ConfidenceLevel,
                    Reason = geminiAnalysis.Reason
                } : null
            };
        }).ToList();

        return new GetUpcomingMatchesResponse
        {
            Data = new PagedResponse<UpcomingMatchDto>
            {
                Offset = query.Offset,
                Limit = query.Limit,
                Total = total,
                Items = finalMatches,
                Summary = new ResponseSummary { TotalStake = 0 } // Placeholder
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
            Status = DetermineStatus(hWins, aWins),
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
            
            bool isHome = IsMatch(m.HomeTeam, teamName);
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

    private string DetermineStatus(int homeWins, int awayWins)
    {
        if (homeWins > awayWins) return "Home Advantage";
        if (awayWins > homeWins) return "Away Advantage";
        return "Balanced";
    }
}
