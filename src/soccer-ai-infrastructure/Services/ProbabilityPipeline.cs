using Microsoft.Extensions.Logging;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;

namespace SoccerAi.Infrastructure.Services;

/// <summary>
/// The single probability flow:
/// Dixon-Coles model → market calibration (Shin-cleaned odds). That's it.
/// No Monte Carlo, no consensus blending, no ML contribution.
/// </summary>
public sealed class ProbabilityPipeline(
    IDixonColesModel dixonColesModel,
    IMarketCalibrationService marketCalibration,
    ILogger<ProbabilityPipeline> logger) : IProbabilityPipeline
{
    public async Task<ProbabilityBundle?> RunAsync(
        Fixture fixture,
        TeamStatsResponse stats,
        CancellationToken ct)
    {
        var dc = await dixonColesModel.CalculateProbabilitiesAsync(
            fixture.LeagueId, fixture.HomeTeamId, fixture.AwayTeamId, fixture.Date, ct);

        if (dc == null)
        {
            logger.LogInformation(
                "Dixon-Coles returned no output for fixture {Id} (insufficient data)", fixture.Id);
            return null;
        }

        var poissonModel = new PoissonModel
        {
            ExpectedHomeGoals = dc.HomeExpectedGoals,
            ExpectedAwayGoals = dc.AwayExpectedGoals,
            ExpectedScoreDifference = dc.HomeExpectedGoals - dc.AwayExpectedGoals,
            HomeWin = dc.HomeWin,
            Draw = dc.Draw,
            AwayWin = dc.AwayWin,
            BTTS = dc.BothTeamScoredGoal,
            Over25 = dc.Over25,
            TwoToThreeGoals = dc.TwoToThreeGoals,
            BttsAndOver25 = dc.BttsAndOver25,
            IsValid = true
        };

        var calibrated = marketCalibration.Calibrate(dc, fixture);

        return new ProbabilityBundle
        {
            Poisson = poissonModel,
            Calibrated = calibrated
        };
    }
}
