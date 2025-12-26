using Microsoft.Extensions.Logging;
using soccer_gpt_application.Interfaces;
using soccer_gpt_application.Models.ML;

namespace soccer_gpt_infrastructure.Services.Features;

/// <summary>
/// Feature engineering service implementing proper ML best practices
/// - Venue-specific historical data (home/away separated)
/// - Time-weighted averages (recent games matter more)
/// - Opponent-adjusted performance
/// - Volatility metrics
/// - League normalization
/// </summary>
public class MatchFeatureBuilder : IMatchFeatureBuilder
{
    private readonly IHistoricalDataRepository _histData;
    private readonly ILogger<MatchFeatureBuilder> _logger;
    
    // Cache for league contexts to avoid repeated calculations
    private readonly Dictionary<string, LeagueContext> _leagueContextCache = new();
    
    public MatchFeatureBuilder(
        IHistoricalDataRepository histData,
        ILogger<MatchFeatureBuilder> logger)
    {
        _histData = histData;
        _logger = logger;
    }
    
    public async Task<MatchFeaturesDto> BuildFeaturesAsync(
        string homeTeam,
        string awayTeam,
        string league,
        DateTime? matchDate = null)
    {
        _logger.LogInformation("Building features for {Home} vs {Away} ({League})", 
            homeTeam, awayTeam, league);
        
        try
        {
            // 1. Get venue-specific historical matches
            var homeMatches = await GetVenueSpecificMatchesAsync(homeTeam, true, league, matchDate);
            var awayMatches = await GetVenueSpecificMatchesAsync(awayTeam, false, league, matchDate);
            
            _logger.LogDebug("Found {HomeCount} home matches, {AwayCount} away matches", 
                homeMatches.Count, awayMatches.Count);
            
            // 2. Calculate attack features
            var homeAttack = CalculateAttackFeatures(homeMatches, homeTeam, true);
            var awayAttack = CalculateAttackFeatures(awayMatches, awayTeam, false);
            
            // 3. Calculate defense features
            var homeDefense = CalculateDefenseFeatures(homeMatches, homeTeam, true);
            var awayDefense = CalculateDefenseFeatures(awayMatches, awayTeam, false);
            
            // 4. Calculate momentum & form
            var homeMomentum = CalculateMomentum(homeMatches, homeTeam, true);
            var awayMomentum = CalculateMomentum(awayMatches, awayTeam, false);
            
            // 5. Calculate fail-to-score rates
            var homeFailToScore = CalculateFailToScoreRate(homeMatches, homeTeam, true);
            var awayFailToScore = CalculateFailToScoreRate(awayMatches, awayTeam, false);
            
            // 6. Get league context
            var leagueContext = await GetLeagueContextAsync(league);
            
            // 7. Build complete feature set
            var features = new MatchFeaturesDto
            {
                HomeTeam = homeTeam,
                AwayTeam = awayTeam,
                League = league,
                
                // Attack features
                HomeAttackStrength = homeAttack.Strength,
                HomeAttackVolatility = homeAttack.Volatility,
                HomeScoringEfficiency = homeAttack.ScoringEfficiency,
                HomeGoalsLast5 = homeAttack.Last5Avg,
                HomeGoalsLast10 = homeAttack.Last10Avg,
                
                AwayAttackStrength = awayAttack.Strength,
                AwayAttackVolatility = awayAttack.Volatility,
                AwayScoringEfficiency = awayAttack.ScoringEfficiency,
                AwayGoalsLast5 = awayAttack.Last5Avg,
                AwayGoalsLast10 = awayAttack.Last10Avg,
                
                // Defense features
                HomeDefenseStrength = homeDefense.Strength,
                HomeDefenseVolatility = homeDefense.Volatility,
                HomeCleanSheetRate = homeDefense.CleanSheetRate,
                HomeConcededLast5 = homeDefense.Last5Avg,
                
                AwayDefenseStrength = awayDefense.Strength,
                AwayDefenseVolatility = awayDefense.Volatility,
                AwayCleanSheetRate = awayDefense.CleanSheetRate,
                AwayConcededLast5 = awayDefense.Last5Avg,
                
                // Momentum & form
                HomeMomentum = homeMomentum,
                AwayMomentum = awayMomentum,
                MomentumGap = Math.Abs(homeMomentum - awayMomentum),
                HomeFailToScoreRate = homeFailToScore,
                AwayFailToScoreRate = awayFailToScore,
                
                // League context
                LeagueAvgGoals = leagueContext.AvgGoals,
                LeagueGoalVolatility = leagueContext.GoalVolatility,
                HomeVsLeagueAttack = homeAttack.Strength / leagueContext.AvgHomeGoals,
                AwayVsLeagueAttack = awayAttack.Strength / leagueContext.AvgAwayGoals,
                HomeVsLeagueDefense = homeDefense.Strength / leagueContext.AvgAwayGoals,
                AwayVsLeagueDefense = awayDefense.Strength / leagueContext.AvgHomeGoals,
                
                // Derived metrics
                ExpectedTotalGoals = homeAttack.Strength + awayAttack.Strength,
                MatchVolatilityIndex = (homeAttack.Volatility + awayAttack.Volatility) / 2.0,
                QualityDifferential = (homeAttack.Strength - awayAttack.Strength)
            };
            
            return features;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building features for {Home} vs {Away}", homeTeam, awayTeam);
            throw;
        }
    }
    
    public async Task<List<MatchFeaturesDto>> BuildFeaturesBatchAsync(
        List<(string HomeTeam, string AwayTeam, string League)> fixtures)
    {
        var features = new List<MatchFeaturesDto>();
        
        foreach (var fixture in fixtures)
        {
            try
            {
                var matchFeatures = await BuildFeaturesAsync(
                    fixture.HomeTeam,
                    fixture.AwayTeam,
                    fixture.League);
                
                features.Add(matchFeatures);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to build features for {Home} vs {Away}",
                    fixture.HomeTeam, fixture.AwayTeam);
            }
        }
        
        return features;
    }
    
    public async Task<LeagueContext> GetLeagueContextAsync(string league)
    {
        // Check cache first
        if (_leagueContextCache.TryGetValue(league, out var cached))
        {
            return cached;
        }
        
        var allMatches = await _histData.GetAllMatchesAsync();
        var leagueMatches = allMatches
            .Where(m => m.League == league)
            .OrderByDescending(m => m.Date)
            .Take(100) // Last 100 matches for league stats
            .ToList();
        
        if (!leagueMatches.Any())
        {
            _logger.LogWarning("No matches found for league {League}, using defaults", league);
            return new LeagueContext
            {
                League = league,
                AvgGoals = 2.5,
                GoalVolatility = 1.2,
                AvgHomeGoals = 1.4,
                AvgAwayGoals = 1.1
            };
        }
        
        var context = new LeagueContext
        {
            League = league,
            AvgGoals = leagueMatches.Average(m => m.FTHG + m.FTAG),
            GoalVolatility = CalculateStdDev(leagueMatches.Select(m => (double)(m.FTHG + m.FTAG))),
            AvgHomeGoals = leagueMatches.Average(m => m.FTHG),
            AvgAwayGoals = leagueMatches.Average(m => m.FTAG)
        };
        
        _leagueContextCache[league] = context;
        return context;
    }
    
    // === Private Helper Methods ===
    
    private async Task<List<HistoricalMatchDto>> GetVenueSpecificMatchesAsync(
        string team,
        bool isHome,
        string league,
        DateTime? beforeDate = null)
    {
        var allMatches = await _histData.GetAllMatchesAsync();
        
        return allMatches
            .Where(m => m.League == league)
            .Where(m => isHome ? m.HomeTeam == team : m.AwayTeam == team)
            .Where(m => !beforeDate.HasValue || m.Date < beforeDate.Value)
            .OrderByDescending(m => m.Date)
            .Take(20) // Last 20 venue-specific matches
            .ToList();
    }
    
    private AttackFeatures CalculateAttackFeatures(
        List<HistoricalMatchDto> matches,
        string team,
        bool isHome)
    {
        if (!matches.Any())
        {
            return new AttackFeatures
            {
                Strength = 1.0,
                Volatility = 1.0,
                Last5Avg = 1.0,
                Last10Avg = 1.0,
                ScoringEfficiency = 1.0
            };
        }
        
        var recentMatches = matches.Take(10).ToList();
        
        // Extract goals with time-decay weighting
        var goalsData = recentMatches.Select((m, idx) => new
        {
            Goals = isHome ? m.FTHG : m.FTAG,
            Shots = isHome ? m.HST : m.AST, // Shots on target
            Weight = Math.Pow(0.9, idx) // Exponential decay: 1.0, 0.9, 0.81, 0.73...
        }).ToList();
        
        // Weighted average
        var weightedAvg = goalsData.Sum(g => g.Goals * g.Weight) / goalsData.Sum(g => g.Weight);
        
        // Volatility (standard deviation)
        var volatility = CalculateStdDev(goalsData.Select(g => (double)g.Goals));
        
        // Last 5 average
        var last5Avg = recentMatches.Take(5).Average(m => isHome ? m.FTHG : m.FTAG);
        
        // Scoring efficiency (goals per shot on target)
        var totalGoals = goalsData.Sum(g => g.Goals);
        var totalShots = goalsData.Sum(g => Math.Max(1, g.Shots)); // Avoid division by zero
        var efficiency = (double)totalGoals / totalShots;
        
        return new AttackFeatures
        {
            Strength = weightedAvg,
            Volatility = volatility,
            Last5Avg = last5Avg,
            Last10Avg = weightedAvg,
            ScoringEfficiency = efficiency
        };
    }
    
    private DefenseFeatures CalculateDefenseFeatures(
        List<HistoricalMatchDto> matches,
        string team,
        bool isHome)
    {
        if (!matches.Any())
        {
            return new DefenseFeatures
            {
                Strength = 1.0,
                Volatility = 1.0,
                CleanSheetRate = 0.0,
                Last5Avg = 1.0
            };
        }
        
        var recentMatches = matches.Take(10).ToList();
        
        // Extract conceded goals with time-decay
        var concededData = recentMatches.Select((m, idx) => new
        {
            Conceded = isHome ? m.FTAG : m.FTHG,
            Weight = Math.Pow(0.9, idx)
        }).ToList();
        
        // Weighted average
        var weightedAvg = concededData.Sum(c => c.Conceded * c.Weight) / concededData.Sum(c => c.Weight);
        
        // Volatility
        var volatility = CalculateStdDev(concededData.Select(c => (double)c.Conceded));
        
        // Clean sheet rate (last 10 matches)
        var cleanSheets = recentMatches.Count(m => (isHome ? m.FTAG : m.FTHG) == 0);
        var cleanSheetRate = (double)cleanSheets / recentMatches.Count;
        
        // Last 5 average
        var last5Avg = recentMatches.Take(5).Average(m => isHome ? m.FTAG : m.FTHG);
        
        return new DefenseFeatures
        {
            Strength = weightedAvg,
            Volatility = volatility,
            CleanSheetRate = cleanSheetRate,
            Last5Avg = last5Avg
        };
    }
    
    private double CalculateMomentum(
        List<HistoricalMatchDto> matches,
        string team,
        bool isHome)
    {
        if (!matches.Any()) return 0.0;
        
        var recentMatches = matches.Take(5).ToList();
        
        var momentumPoints = recentMatches.Select((m, idx) =>
        {
            var teamGoals = isHome ? m.FTHG : m.FTAG;
            var oppGoals = isHome ? m.FTAG : m.FTHG;
            
            // Points: Win=3, Draw=1, Loss=0
            var points = teamGoals > oppGoals ? 3.0 :
                        teamGoals == oppGoals ? 1.0 : 0.0;
            
            // Weight recent games more
            var weight = Math.Pow(0.85, idx);
            
            return points * weight;
        }).ToList();
        
        // Normalize to [-1, 1] range
        var totalWeightedPoints = momentumPoints.Sum();
        var maxPossible = 3.0 * momentumPoints.Count; //Continued...
        
        return (totalWeightedPoints / maxPossible) * 2.0 - 1.0; // Scale to [-1, 1]
    }
    
    private double CalculateFailToScoreRate(
        List<HistoricalMatchDto> matches,
        string team,
        bool isHome)
    {
        if (!matches.Any()) return 0.0;
        
        var recentMatches = matches.Take(10).ToList();
        var blanks = recentMatches.Count(m => (isHome ? m.FTHG : m.FTAG) == 0);
        
        return (double)blanks / recentMatches.Count;
    }
    
    private double CalculateStdDev(IEnumerable<double> values)
    {
        var valuesList = values.ToList();
        if (!valuesList.Any()) return 0.0;
        
        var avg = valuesList.Average();
        var sumOfSquares = valuesList.Sum(v => Math.Pow(v - avg, 2));
        
        return Math.Sqrt(sumOfSquares / valuesList.Count);
    }
}
