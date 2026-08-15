using FluentAssertions;
using SoccerAi.Application.Models;

namespace soccer_ai_unit_tests.Api;

/// <summary>
/// One call per fixture, graded once. The per-market grid it replaced could
/// read as mostly-correct on a match the system got wrong, which is the number
/// users would quote back.
/// </summary>
public class HeadlinePredictionTests
{
    /// <summary>Mirrors AnalysisResponseMapper.BuildHeadline, which is private.</summary>
    private static HeadlinePrediction? Build(WeightedPrediction? p, MatchResult? result)
    {
        if (p is null) return null;

        var candidates = new (string Market, string Selection, double Probability, bool? Correct)[]
        {
            p.Over25
                ? ("over_2_5", "Over 2.5 Goals", p.Over25Prob, result is null ? null : result.ActualOver25 == true)
                : ("under_2_5", "Under 2.5 Goals", 1 - p.Over25Prob, result is null ? null : result.ActualOver25 == false),

            p.BTTS
                ? ("btts", "Both Teams To Score", p.BTTSProb, result is null ? null : result.ActualBtts == true)
                : ("no_btts", "Not Both Teams To Score", 1 - p.BTTSProb, result is null ? null : result.ActualBtts == false),

            p.MatchWinner.Equals("home", StringComparison.OrdinalIgnoreCase)
                ? ("home_win", "Home Win", p.HomeProb, result is null ? null : result.ActualWinner == "home")
                : p.MatchWinner.Equals("away", StringComparison.OrdinalIgnoreCase)
                    ? ("away_win", "Away Win", p.AwayProb, result is null ? null : result.ActualWinner == "away")
                    : ("draw", "Draw", p.DrawProb, result is null ? null : result.ActualWinner == "draw"),
        };

        // A market whose probability is exactly 0 was not computed — treat it as
        // absent rather than as a certainty. Without this the complement of an
        // unset probability is 1.0, and a market the model never priced wins the
        // headline slot as a 100% confident call.
        var best = candidates
            .Where(c => c.Probability is > 0 and < 1)
            .OrderByDescending(c => c.Probability)
            .FirstOrDefault();

        if (best.Market is null) return null;

        return new HeadlinePrediction
        {
            Market = best.Market,
            Selection = best.Selection,
            Probability = Math.Round(best.Probability, 4),
            IsCorrect = best.Correct,
        };
    }

    private static MatchResult Result(int home, int away) => new()
    {
        ActualScore = $"{home}:{away}",
        HomeGoals = home,
        AwayGoals = away,
        TotalGoals = home + away,
        ActualBtts = home > 0 && away > 0,
        ActualOver25 = home + away > 2,
        ActualWinner = home > away ? "home" : home < away ? "away" : "draw",
    };

    [Fact]
    public void The_highest_probability_market_becomes_the_single_call()
    {
        var p = new WeightedPrediction
        {
            Over25 = true, Over25Prob = 0.58,
            BTTS = true, BTTSProb = 0.55,
            MatchWinner = "home", HomeProb = 0.71,
        };

        var headline = Build(p, result: null)!;

        headline.Market.Should().Be("home_win");
        headline.Probability.Should().Be(0.71);
        headline.IsCorrect.Should().BeNull("the fixture has not been played");
    }

    /// <summary>
    /// A 30% "over" is a 70% "under". Ranking on the raw over-probability would
    /// pick a market the model actively disbelieves.
    /// </summary>
    [Fact]
    public void A_negative_lean_is_ranked_on_the_side_the_model_actually_backs()
    {
        var p = new WeightedPrediction
        {
            Over25 = false, Over25Prob = 0.30,
            BTTS = false, BTTSProb = 0.45,
            MatchWinner = "home", HomeProb = 0.40,
        };

        var headline = Build(p, result: null)!;

        headline.Market.Should().Be("under_2_5");
        headline.Selection.Should().Be("Under 2.5 Goals");
        headline.Probability.Should().Be(0.70);
    }

    [Fact]
    public void A_landed_call_grades_correct()
    {
        var p = new WeightedPrediction
        {
            Over25 = true, Over25Prob = 0.80,
            BTTS = true, BTTSProb = 0.50,
            MatchWinner = "home", HomeProb = 0.4,
        };

        Build(p, Result(2, 1))!.IsCorrect.Should().BeTrue();
    }

    [Fact]
    public void A_missed_call_grades_incorrect()
    {
        var p = new WeightedPrediction
        {
            Over25 = true, Over25Prob = 0.80,
            BTTS = true, BTTSProb = 0.50,
            MatchWinner = "home", HomeProb = 0.4,
        };

        Build(p, Result(1, 0))!.IsCorrect.Should().BeFalse();
    }

    /// <summary>
    /// The case the per-market grid got wrong: the system's actual call misses
    /// while two other markets happen to land. One call, one verdict.
    /// </summary>
    [Fact]
    public void Other_markets_landing_does_not_rescue_a_missed_call()
    {
        var p = new WeightedPrediction
        {
            Over25 = true, Over25Prob = 0.55,
            BTTS = true, BTTSProb = 0.52,
            MatchWinner = "away", AwayProb = 0.66,
        };

        // 2-1 home: over hit, BTTS hit — but the call was an away win.
        var headline = Build(p, Result(2, 1))!;

        headline.Market.Should().Be("away_win");
        headline.IsCorrect.Should().BeFalse();
    }

    [Fact]
    public void A_draw_call_is_graded_against_a_draw()
    {
        var p = new WeightedPrediction
        {
            Over25 = false, Over25Prob = 0.45,
            BTTS = false, BTTSProb = 0.48,
            MatchWinner = "draw", DrawProb = 0.62,
        };

        Build(p, Result(1, 1))!.Market.Should().Be("draw");
        Build(p, Result(1, 1))!.IsCorrect.Should().BeTrue();
        Build(p, Result(2, 0))!.IsCorrect.Should().BeFalse();
    }

    [Fact]
    public void No_prediction_means_no_headline()
    {
        Build(p: null, result: Result(1, 1)).Should().BeNull();
    }
}
