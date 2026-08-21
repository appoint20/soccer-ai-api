using FluentAssertions;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Interfaces;
using SoccerAi.Application.Models;
using SoccerAi.Application.Services.Analysis;

namespace soccer_ai_unit_tests.Api;

/// <summary>
/// Per-market verdicts and the settlement status, driven through the real
/// mapper rather than a copy of its rules.
///
/// The client scores a pick on the market it was made in, so judging an
/// Over 2.5 call by the 1X2 outcome is a bug. It also has to tell a fixture
/// that was never played from one the model got wrong: counting a postponed
/// match as a loss understates the record.
/// </summary>
public class MatchResultVerdictTests
{
    private static MatchAnalysis Map(
        int homeGoals, int awayGoals, string fixtureStatus, WeightedPrediction? prediction)
    {
        var fixture = new Fixture
        {
            Id = 1181423,
            Date = new DateTimeOffset(2026, 8, 21, 18, 30, 0, TimeSpan.Zero),
            Status = fixtureStatus,
            HomeGoal = homeGoals,
            AwayGoal = awayGoals,
            HomeTeamId = 51,
            AwayTeamId = 45,
            LeagueId = 39,
        };

        var analysis = new FixtureAnalysisResult
        {
            FixtureId = fixture.Id,
            TeamStats = new TeamStatsResponse(),
            Models = new StatisticalModels(),
            H2H = HeadToHeadModel.Empty,
            Decisions = new DecisionServiceResult(),
            LeagueName = "Premier League",
            Prediction = prediction,
        };

        return AnalysisResponseMapper.MapToResponse(
            fixture,
            analysis,
            new Team { ApiId = 51, Name = "Brighton" },
            new Team { ApiId = 45, Name = "Everton" },
            aiAnalysis: null);
    }

    /// <summary>Called over 2.5 and both to score; 2-1 delivers both.</summary>
    private static WeightedPrediction OverAndBttsHome => new()
    {
        Over25 = true,
        BTTS = true,
        TwoToThreeGoals = true,
        MatchWinner = "home",
    };

    private static bool? VerdictFor(MatchAnalysis analysis, string market) =>
        analysis.Result?.Markets.FirstOrDefault(m => m.Market == market)?.Correct;

    [Fact]
    public void GoalsMarketsAreJudgedOnGoals_NotOnTheWinner()
    {
        // 1-2: over 2.5 hit and both teams scored, but the home call lost. The
        // goals markets must still read as correct.
        var analysis = Map(1, 2, "FT", OverAndBttsHome);

        VerdictFor(analysis, "over25").Should().BeTrue();
        VerdictFor(analysis, "btts").Should().BeTrue();
        VerdictFor(analysis, "match_winner").Should().BeFalse();
    }

    [Fact]
    public void CorrectlyPredictingAMarketWillNotHit_CountsAsCorrect()
    {
        var predictedNeither = new WeightedPrediction
        {
            Over25 = false,
            BTTS = false,
            TwoToThreeGoals = false,
            MatchWinner = "home",
        };

        var analysis = Map(1, 0, "FT", predictedNeither);

        VerdictFor(analysis, "over25").Should().BeTrue("1-0 is under 2.5, which is what was called");
        VerdictFor(analysis, "btts").Should().BeTrue("only one side scored, as called");
    }

    [Fact]
    public void Under25SharesTheOver25Verdict()
    {
        // One binary call: getting "over" wrong is the same event as getting
        // "under" wrong, so the two verdicts can never disagree.
        var analysis = Map(3, 1, "FT", OverAndBttsHome);

        VerdictFor(analysis, "under25").Should().Be(VerdictFor(analysis, "over25"));
    }

    [Fact]
    public void DrawIsJudgedSeparatelyFromTheWinner()
    {
        var analysis = Map(1, 1, "FT", OverAndBttsHome);

        VerdictFor(analysis, "match_winner").Should().BeFalse("home was called and it finished level");
        VerdictFor(analysis, "draw").Should().BeFalse("not-a-draw was implied, and it was a draw");
    }

    [Fact]
    public void EveryAuditableMarketCanBeScored()
    {
        var analysis = Map(2, 1, "FT", OverAndBttsHome);

        analysis.Result!.Markets.Select(m => m.Market)
            .Should().BeEquivalentTo(["btts", "over25", "under25", "goals_2_3", "match_winner", "draw"]);
    }

    [Fact]
    public void WithoutAPrediction_NothingIsJudged()
    {
        // Omission means "not judged" and renders as no icon. Sending false
        // here would report a pick that was never made as one that lost.
        var analysis = Map(2, 1, "FT", prediction: null);

        analysis.Result!.Markets.Should().BeEmpty();
        analysis.Result.Status.Should().Be(ResultStatus.Settled);
    }

    [Theory]
    [InlineData("FT", ResultStatus.Settled)]
    [InlineData("AET", ResultStatus.Settled)]
    [InlineData("PEN", ResultStatus.Settled)]
    [InlineData("ABD", ResultStatus.Abandoned)]
    [InlineData("AWD", ResultStatus.Void)]
    [InlineData("WO", ResultStatus.Void)]
    [InlineData("CANC", ResultStatus.Void)]
    [InlineData("PST", ResultStatus.Postponed)]
    [InlineData("SUSP", ResultStatus.Postponed)]
    public void FixtureStatusMapsToASettlementStatus(string fixtureStatus, string expected)
    {
        var analysis = Map(2, 1, fixtureStatus, OverAndBttsHome);

        analysis.Result!.Status.Should().Be(expected);
    }

    [Theory]
    [InlineData("NS")]
    [InlineData("1H")]
    [InlineData("HT")]
    public void AnUnplayedFixtureHasNoResultAtAll(string fixtureStatus)
    {
        Map(0, 0, fixtureStatus, OverAndBttsHome).Result.Should().BeNull();
    }

    [Fact]
    public void AnAbandonedMatchIsNotScoredOnTheGoalsItHappenedToHave()
    {
        // 3-1 at the point it was abandoned would otherwise read as a correct
        // over 2.5 call on a match that never produced a market outcome.
        var analysis = Map(3, 1, "ABD", OverAndBttsHome);

        analysis.Result!.Status.Should().Be(ResultStatus.Abandoned);
        analysis.Result.Markets.Should().BeEmpty();
        analysis.Result.IsCorrect.Should().BeFalse();
        analysis.Result.IsOver25Correct.Should().BeNull("an unfinished match settles nothing");
        analysis.Result.IsBttsCorrect.Should().BeNull();
    }

    [Fact]
    public void AWalkoverIsVoid_NotAWin()
    {
        var analysis = Map(3, 0, "WO", OverAndBttsHome);

        analysis.Result!.Status.Should().Be(ResultStatus.Void);
        analysis.Result.Markets.Should().BeEmpty();
    }

    [Fact]
    public void LegacyFlagsStillAgreeWithTheMarketVerdicts()
    {
        // The client prefers markets[] but keeps reading the old fields, so the
        // two must not tell different stories about the same fixture.
        var analysis = Map(2, 1, "FT", OverAndBttsHome);
        var result = analysis.Result!;

        result.IsOver25Correct.Should().Be(VerdictFor(analysis, "over25"));
        result.IsBttsCorrect.Should().Be(VerdictFor(analysis, "btts"));
        result.IsCorrect.Should().Be(VerdictFor(analysis, "match_winner")!.Value);
    }
}
