namespace SoccerAi.Application.Services;

using SoccerAi.Application.Models;

/// <summary>
/// Boosts Over 2.5 / BTTS probabilities when H2H data strongly contradicts recent form.
/// 
/// Example: Preston vs Watford
///   - Recent form: BTTS 30%, Over 2.5 30% → model says "no"
///   - H2H (5+ matches): BTTS 60%, Over 2.5 60% → fixture-specific pattern exists
///   
/// The divergence between H2H and form signals a VALUE opportunity that
/// pure form-based models miss. This class detects that divergence and
/// applies a proportional probability boost.
/// </summary>
public static class H2HDivergenceBoost
{
    /// <summary>Minimum H2H matches required for a reliable divergence signal.</summary>
    private const int MinH2HMatches = 3;
    
    /// <summary>
    /// Minimum divergence (H2H rate - form rate) to trigger a boost.
    /// At 0.15 (15%), H2H must clearly disagree with form.
    /// </summary>
    private const double MinDivergence = 0.15;
    
    /// <summary>
    /// Maximum boost applied to the probability. Capped at 12% to avoid over-correction.
    /// </summary>
    private const double MaxBoost = 0.12;
    
    /// <summary>
    /// Weight given to the H2H signal. Higher = more H2H influence.
    /// 0.40 means H2H contributes up to 40% of the divergence as boost.
    /// </summary>
    private const double H2HWeight = 0.40;

    /// <summary>
    /// Calculate the probability boost for Over 2.5 based on H2H divergence from form.
    /// </summary>
    public static double Over25Boost(HeadToHeadModel h2h, TeamStatsResponse stats)
    {
        if (!h2h.IsValid || h2h.MatchesAnalyzed < MinH2HMatches) return 0;

        // Average of both teams' Over 2.5 rate from recent form (last 7)
        double formRate = (stats.Home.Over25RateLast7 + stats.Away.Over25RateLast7) / 2.0;
        double h2hRate = h2h.Over25Rate;

        double divergence = h2hRate - formRate;
        
        if (divergence < MinDivergence) return 0;

        // Scale boost by H2H match count confidence (more matches = more reliable)
        double matchConfidence = Math.Min(h2h.MatchesAnalyzed / 5.0, 1.0);
        
        // Boost = divergence * weight * match_confidence, capped at MaxBoost
        double boost = divergence * H2HWeight * matchConfidence;
        return Math.Min(boost, MaxBoost);
    }

    /// <summary>
    /// Calculate the probability boost for BTTS based on H2H divergence from form.
    /// </summary>
    public static double BTTSBoost(HeadToHeadModel h2h, TeamStatsResponse stats)
    {
        if (!h2h.IsValid || h2h.MatchesAnalyzed < MinH2HMatches) return 0;

        double formRate = (stats.Home.BTTSRateLast7 + stats.Away.BTTSRateLast7) / 2.0;
        double h2hRate = h2h.BTTSRate;

        double divergence = h2hRate - formRate;
        
        if (divergence < MinDivergence) return 0;

        double matchConfidence = Math.Min(h2h.MatchesAnalyzed / 5.0, 1.0);
        
        double boost = divergence * H2HWeight * matchConfidence;
        return Math.Min(boost, MaxBoost);
    }
}
