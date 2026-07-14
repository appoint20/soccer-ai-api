using Microsoft.Extensions.Options;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Options;

namespace SoccerAi.Application.Services;

/// <summary>
/// Blends Dixon-Coles probabilities with Shin-margin-removed market
/// probabilities: final_p = w_model × p_DC + w_market × p_market.
///
/// Odds handling per market:
/// - 1X2: three-way Shin over (home, draw, away) when all three exist.
/// - Over/Under 2.5: two-way Shin over (over, under) when both exist.
/// - BTTS: only the "yes" odd is stored, so the margin cannot be estimated;
///   naive implied probability (1/odds) is used and this limitation is
///   accepted deliberately.
/// - 2-3 goals: no odds exist for this market → pure model probability.
/// - Any missing/invalid odds → that market keeps the pure model probability.
/// </summary>
public sealed class MarketCalibrator(IOptions<CalibrationOptions> options) : IMarketCalibrationService
{
    private readonly CalibrationOptions _opt = options.Value;

    public CalibratedProbabilities Calibrate(PoissonProbabilities model, Fixture fixture)
    {
        var w = Math.Clamp(_opt.MarketWeight, 0, 1);
        var usedOdds = false;

        // ── 1X2: three-way Shin ──
        double pHome = model.HomeWin, pDraw = model.Draw, pAway = model.AwayWin;
        if (IsValidOdd(fixture.HomeWinOdds) && IsValidOdd(fixture.DrawOdds) && IsValidOdd(fixture.AwayWinOdds))
        {
            var market = ShinMarginRemoval.TrueProbabilities(
                [fixture.HomeWinOdds!.Value, fixture.DrawOdds!.Value, fixture.AwayWinOdds!.Value]);

            pHome = Blend(model.HomeWin, market[0], w);
            pDraw = Blend(model.Draw, market[1], w);
            pAway = Blend(model.AwayWin, market[2], w);

            // Keep 1X2 a proper distribution after blending.
            var total = pHome + pDraw + pAway;
            if (total > 0) { pHome /= total; pDraw /= total; pAway /= total; }
            usedOdds = true;
        }

        // ── Over/Under 2.5: two-way Shin ──
        var pOver = model.Over25;
        if (IsValidOdd(fixture.Over25Odds) && IsValidOdd(fixture.Under25Odds))
        {
            var marketOver = ShinMarginRemoval.TrueProbability(
                fixture.Over25Odds!.Value, fixture.Under25Odds!.Value);
            pOver = Blend(model.Over25, marketOver, w);
            usedOdds = true;
        }
        else if (IsValidOdd(fixture.Over25Odds))
        {
            pOver = Blend(model.Over25, NaiveImplied(fixture.Over25Odds!.Value), w);
            usedOdds = true;
        }

        // ── BTTS: naive implied (no "no" odds stored) ──
        var pBtts = model.BothTeamScoredGoal;
        if (IsValidOdd(fixture.BttsYesOdds))
        {
            pBtts = Blend(model.BothTeamScoredGoal, NaiveImplied(fixture.BttsYesOdds!.Value), w);
            usedOdds = true;
        }

        return new CalibratedProbabilities
        {
            HomeWin = Math.Clamp(pHome, 0, 1),
            Draw = Math.Clamp(pDraw, 0, 1),
            AwayWin = Math.Clamp(pAway, 0, 1),
            Over25 = Math.Clamp(pOver, 0, 1),
            Btts = Math.Clamp(pBtts, 0, 1),
            TwoToThreeGoals = Math.Clamp(model.TwoToThreeGoals, 0, 1), // no odds market exists
            UsedMarketOdds = usedOdds
        };
    }

    private static double Blend(double modelP, double marketP, double marketWeight) =>
        (1 - marketWeight) * modelP + marketWeight * marketP;

    private static double NaiveImplied(double odds) => 1.0 / odds;

    private static bool IsValidOdd(double? odds) => odds is > 1.0;
}
