using Microsoft.Extensions.DependencyInjection;

namespace SoccerAi.Infrastructure.Configuration;

/// <summary>
/// Configuration for decision service thresholds.
/// Can be loaded from appsettings.json and tuned without code changes.
/// </summary>
public class DecisionThresholdsConfig
{
    // Probability thresholds
    public double MinProbabilityOver25 { get; set; } = 0.52;
    public double MinProbabilityBTTS { get; set; } = 0.52;
    public double MinProbabilityLowScoring { get; set; } = 0.30;
    public double MaxProbabilityForLowScoring { get; set; } = 0.40;
    public double MinProbabilityDraw { get; set; } = 0.50;
    public double MinProbabilityMatchWinner { get; set; } = 0.50;
    public double MinProbability2To3Goals { get; set; } = 0.45;

    // Statistical model thresholds
    public double MinStatisticalModelProb { get; set; } = 0.52;
    public double StrongStatisticalModelProb { get; set; } = 0.65;
    public double MinModelAgreementDiff { get; set; } = 0.10;

    // Team statistics thresholds
    public int MinScoredInLast3Spec { get; set; } = 2;
    public double MinScoredRate { get; set; } = 0.50;
    public int MinScoredInLast3Overall { get; set; } = 2;
    public double MinCleanSheetRateForDefense { get; set; } = 0.25;
    public double MaxCleanSheetRateForBTTS { get; set; } = 0.60;
    public double MaxFailedToScoreRate { get; set; } = 0.50;
    public double MaxJointAvgGoalsForLowScoring { get; set; } = 2.5;
    public double MinTeamFormPoints { get; set; } = 1.0;

    // Odds thresholds
    public double MinValueOdds { get; set; } = 1.90;
    public double MaxDrawOddsForQualification { get; set; } = 5.00;
    public double MinDrawOddsForValue { get; set; } = 2.50;
    public double MinOddsEdgeRequired { get; set; } = 0.05;
    public double MinOddsForValue { get; set; } = 1.50;

    // Historical data thresholds
    public int MinH2HMatches { get; set; } = 5;
    public int MinH2HMatchesForTrap { get; set; } = 2;
    public int MaxDaysGapForTrap { get; set; } = 730;
    public double MinH2HDrawRate { get; set; } = 0.20;
    public double MinH2HWinRateForWinner { get; set; } = 0.35;
    public double MinDrawRateInForm { get; set; } = 0.15;

    // Qualification thresholds
    public double MinQualificationProb { get; set; } = 0.60;
    public double MinOverallFormScore { get; set; } = 0.0;

    // Cross-market rule thresholds
    public double MinConfidenceDiffForBlock { get; set; } = 0.10;
    public double StrongSignalThreshold { get; set; } = 0.35;
    public double WeakSignalThreshold { get; set; } = 0.25;

    // Form validation thresholds
    public double MinAvgTotalGoalsForOver25 { get; set; } = 2.0;
    public double MinTeamGoalsScoredForOver25 { get; set; } = 0.8;
    public double MaxExpectedTotalGoalsForLowScoring { get; set; } = 2.0;
    public double MaxTeamGoalsScoredForLowScoring { get; set; } = 1.2;

    public static DecisionThresholdsConfig CreateConservative() => new()
    {
        MinProbabilityOver25 = 0.60,
        MinProbabilityBTTS = 0.60,
        MinProbabilityLowScoring = 0.35,
        MinProbabilityDraw = 0.55,
        MinProbabilityMatchWinner = 0.60,
        MinStatisticalModelProb = 0.55,
        StrongStatisticalModelProb = 0.70,
        MinScoredInLast3Spec = 3,
        MinScoredRate = 0.70,
        MinScoredInLast3Overall = 3,
        MinValueOdds = 2.20,
        MinOddsEdgeRequired = 0.10,
        MinH2HMatches = 7,
        MinH2HWinRateForWinner = 0.50
    };

    public static DecisionThresholdsConfig CreateAggressive() => new()
    {
        MinProbabilityOver25 = 0.52,
        MinProbabilityBTTS = 0.52,
        MinProbabilityLowScoring = 0.25,
        MinProbabilityDraw = 0.48,
        MinProbabilityMatchWinner = 0.52,
        MinStatisticalModelProb = 0.50,
        StrongStatisticalModelProb = 0.60,
        MinScoredInLast3Spec = 2,
        MinScoredRate = 0.55,
        MinScoredInLast3Overall = 2,
        MinValueOdds = 1.80,
        MinOddsEdgeRequired = 0.03,
        MinH2HMatches = 3,
        MinH2HWinRateForWinner = 0.35
    };

    public static DecisionThresholdsConfig CreateBalanced() => new();

}

public static class DecisionConfigurationExtensions
{
    public static IServiceCollection AddDecisionThresholds(
        this IServiceCollection services,
        Action<DecisionThresholdsConfig>? configure = null)
    {
        var config = DecisionThresholdsConfig.CreateBalanced();
        configure?.Invoke(config);
        services.AddSingleton(config);
        return services;
    }
}
