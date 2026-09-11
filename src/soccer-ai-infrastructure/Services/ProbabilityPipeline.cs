using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// The single probability flow.
///
/// Preferred path (hybrid):
///   rolling features → two learned goal rates → Dixon-Coles score matrix.
/// The learned rates already take the bookmaker's prices as inputs, so the
/// output is NOT blended with the market again — doing so would count the same
/// information twice and pull every estimate back toward the price, which is
/// precisely the flattening that made the previous model's probabilities
/// cluster in a six-point band.
///
/// Fallback path (classic), used whenever no trained model is on disk:
///   Dixon-Coles → market calibration (Shin-cleaned odds), exactly as before.
///
/// Either way there is no Monte Carlo and no consensus blending.
/// </summary>
public sealed class ProbabilityPipeline(
    IDixonColesModel dixonColesModel,
    IMarketCalibrationService marketCalibration,
    IGoalRateForecaster goalRateForecaster,
    ILogger<ProbabilityPipeline> logger) : IProbabilityPipeline
{
    public async Task<ProbabilityBundle?> RunAsync(
        Fixture fixture,
        TeamStatsResponse stats,
        CancellationToken ct)
    {
        var hybrid = await TryHybridAsync(fixture, ct);
        if (hybrid is not null) return hybrid;

        var dc = await dixonColesModel.CalculateProbabilitiesAsync(
            fixture.LeagueId, fixture.HomeTeamId, fixture.AwayTeamId, fixture.Date, ct);

        if (dc == null)
        {
            logger.LogInformation(
                "Dixon-Coles returned no output for fixture {Id} (insufficient data)", fixture.Id);
            return null;
        }

        return new ProbabilityBundle
        {
            ModelVersion = "dixon-coles-market-v1",
            Poisson = ToPoissonModel(dc),
            Calibrated = marketCalibration.Calibrate(dc, fixture)
        };
    }

    /// <summary>
    /// Run the learned model, or return null so the caller falls back.
    /// </summary>
    /// <remarks>
    /// A failure here must never take a fixture off the board: the classic
    /// model is still a working estimator, and silently losing every pick
    /// because a model file is corrupt would be far worse than serving
    /// slightly flatter probabilities for a day.
    /// </remarks>
    private async Task<ProbabilityBundle?> TryHybridAsync(Fixture fixture, CancellationToken ct)
    {
        try
        {
            var forecast = await goalRateForecaster.ForecastAsync(fixture, ct);
            if (forecast is null) return null;

            var p = forecast.Probabilities;

            return new ProbabilityBundle
            {
                ModelVersion = forecast.ModelVersion ?? "hybrid-unknown",
                Poisson = ToPoissonModel(p),
                Calibrated = new CalibratedProbabilities
                {
                    HomeWin = p.HomeWin,
                    Draw = p.Draw,
                    AwayWin = p.AwayWin,
                    Over25 = p.Over25,
                    Btts = p.BothTeamScoredGoal,
                    TwoToThreeGoals = p.TwoToThreeGoals,
                    // The learned rates consumed the prices as features, so the
                    // market view is already inside these numbers.
                    UsedMarketOdds = true
                }
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "[GoalRate] Hybrid forecast failed for fixture {Id} — using Dixon-Coles", fixture.Id);
            return null;
        }
    }

    private static PoissonModel ToPoissonModel(PoissonProbabilities p) => new()
    {
        ExpectedHomeGoals = p.HomeExpectedGoals,
        ExpectedAwayGoals = p.AwayExpectedGoals,
        ExpectedScoreDifference = p.HomeExpectedGoals - p.AwayExpectedGoals,
        HomeWin = p.HomeWin,
        Draw = p.Draw,
        AwayWin = p.AwayWin,
        BTTS = p.BothTeamScoredGoal,
        Over25 = p.Over25,
        TwoToThreeGoals = p.TwoToThreeGoals,
        BttsAndOver25 = p.BttsAndOver25,
        IsValid = true
    };
}
