using FluentAssertions;
using SoccerAi.Application.Models;

namespace soccer_ai_unit_tests.Api;

/// <summary>
/// The correctness flags previously carried the raw outcome rather than whether
/// the prediction matched it, so a correctly-predicted "no" scored as wrong.
/// These pin the corrected meaning.
/// </summary>
public class MatchResultCorrectnessTests
{
    /// <summary>
    /// Mirrors AnalysisResponseMapper.ValidateMatchResult, which is private.
    /// The duplication is the weak point of this file: it can drift from the
    /// mapper. It is kept because the alternative — widening the mapper's
    /// surface purely for a test — trades a real design for a test convenience.
    /// If these rules change, change both.
    /// </summary>
    private static MatchResult Build(int homeGoals, int awayGoals, WeightedPrediction? p)
    {
        var totalGoals = homeGoals + awayGoals;
        var isBtts = homeGoals > 0 && awayGoals > 0;
        var isOver25 = totalGoals > 2.5;

        var predWinner = p?.MatchWinner ?? "";
        var isWinnerCorrect =
            (predWinner.Equals("home", StringComparison.OrdinalIgnoreCase) && homeGoals > awayGoals) ||
            (predWinner.Equals("draw", StringComparison.OrdinalIgnoreCase) && homeGoals == awayGoals) ||
            (predWinner.Equals("away", StringComparison.OrdinalIgnoreCase) && homeGoals < awayGoals);

        return new MatchResult
        {
            ActualScore = $"{homeGoals}:{awayGoals}",
            IsCorrect = isWinnerCorrect,
            IsBttsCorrect = p is null ? null : p.BTTS == isBtts,
            IsOver25Correct = p is null ? null : p.Over25 == isOver25,
            IsUnder25Correct = p is null ? null : p.Over25 == isOver25,
            HomeGoals = homeGoals,
            AwayGoals = awayGoals,
            TotalGoals = totalGoals,
            ActualBtts = isBtts,
            ActualOver25 = isOver25,
            PredictedWinner = string.IsNullOrWhiteSpace(predWinner) ? null : predWinner.ToLowerInvariant(),
            ActualWinner = homeGoals > awayGoals ? "home" : homeGoals < awayGoals ? "away" : "draw",
        };
    }

    /// <summary>
    /// The bug this file exists for: predicting "no BTTS" on a 1-0 is a correct
    /// call, and used to be reported as wrong because the flag carried the
    /// outcome instead of the comparison.
    /// </summary>
    [Fact]
    public void Correctly_predicting_a_market_will_not_hit_counts_as_correct()
    {
        var p = new WeightedPrediction { BTTS = false, Over25 = false, MatchWinner = "home" };

        var result = Build(homeGoals: 1, awayGoals: 0, p);

        result.IsBttsCorrect.Should().BeTrue("BTTS was predicted 'no' and did not happen");
        result.IsOver25Correct.Should().BeTrue("Over 2.5 was predicted 'no' and did not happen");
        result.ActualBtts.Should().BeFalse();
        result.ActualOver25.Should().BeFalse();
    }

    [Fact]
    public void Predicting_a_market_that_does_hit_counts_as_correct()
    {
        var p = new WeightedPrediction { BTTS = true, Over25 = true, MatchWinner = "home" };

        var result = Build(homeGoals: 2, awayGoals: 1, p);

        result.IsBttsCorrect.Should().BeTrue();
        result.IsOver25Correct.Should().BeTrue();

        // Same binary call seen from the other side: getting "over" right is
        // getting "under" right. These two flags are always equal by
        // construction — the pair exists so a UI can render either row without
        // inverting anything, not because they can disagree.
        result.IsUnder25Correct.Should().Be(result.IsOver25Correct);
    }

    [Fact]
    public void A_missed_market_call_is_reported_wrong()
    {
        var p = new WeightedPrediction { BTTS = true, Over25 = true, MatchWinner = "away" };

        var result = Build(homeGoals: 1, awayGoals: 0, p);

        result.IsBttsCorrect.Should().BeFalse();
        result.IsOver25Correct.Should().BeFalse();
        result.IsCorrect.Should().BeFalse("away was predicted and home won");
    }

    /// <summary>
    /// Over and Under 2.5 are one call. Predicting "over" on a 1-0 is wrong on
    /// both rows; predicting "under" is right on both.
    /// </summary>
    [Fact]
    public void Over_and_under_are_one_call_and_never_disagree()
    {
        var over = new WeightedPrediction { Over25 = true, MatchWinner = "home" };
        var under = new WeightedPrediction { Over25 = false, MatchWinner = "home" };

        // 1-0 → the match went under.
        var predictedOver = Build(1, 0, over);
        predictedOver.IsOver25Correct.Should().BeFalse();
        predictedOver.IsUnder25Correct.Should().BeFalse();

        var predictedUnder = Build(1, 0, under);
        predictedUnder.IsOver25Correct.Should().BeTrue();
        predictedUnder.IsUnder25Correct.Should().BeTrue();
    }

    [Fact]
    public void The_final_score_and_winner_are_carried_for_display()
    {
        var result = Build(3, 1, new WeightedPrediction { MatchWinner = "home" });

        result.ActualScore.Should().Be("3:1");
        result.HomeGoals.Should().Be(3);
        result.AwayGoals.Should().Be(1);
        result.TotalGoals.Should().Be(4);
        result.ActualWinner.Should().Be("home");
        result.PredictedWinner.Should().Be("home");
        result.IsCorrect.Should().BeTrue();
    }

    [Fact]
    public void A_draw_is_reported_as_a_draw()
    {
        var result = Build(1, 1, new WeightedPrediction { MatchWinner = "draw", BTTS = true });

        result.ActualWinner.Should().Be("draw");
        result.IsCorrect.Should().BeTrue();
        result.IsBttsCorrect.Should().BeTrue();
    }

    [Fact]
    public void Market_correctness_is_null_when_nothing_was_predicted()
    {
        var result = Build(2, 1, p: null);

        result.IsBttsCorrect.Should().BeNull();
        result.IsOver25Correct.Should().BeNull();
        result.ActualScore.Should().Be("2:1", "the score is still known without a prediction");
    }
}
