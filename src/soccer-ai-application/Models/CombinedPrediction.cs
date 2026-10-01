using System.Text.Json.Serialization;

namespace SoccerAi.Application.Models;

public sealed record CombinedMarkets
{
    public double? HomeWin { get; init; }
    public double? Draw { get; init; }
    public double? AwayWin { get; init; }
    public double? Btts { get; init; }
    public double? Over25 { get; init; }
    public double? TwoToThreeGoals { get; init; }
    public double? BttsAndOver25 { get; init; }
    public double? ExpectedGoals { get; init; }

    [JsonIgnore] public bool HasOutcomes => Valid(HomeWin) && Valid(Draw) && Valid(AwayWin)
        && Math.Abs(HomeWin!.Value + Draw!.Value + AwayWin!.Value - 1) <= .011;
    [JsonIgnore] public bool HasGoals => Valid(Btts) && Valid(Over25) && Valid(TwoToThreeGoals)
        && Valid(BttsAndOver25) && ExpectedGoals is >= 0 and <= 15
        && BttsAndOver25 <= Math.Min(Btts!.Value, Over25!.Value) + 1e-8
        && BttsAndOver25 >= Math.Max(0, Btts.Value + Over25.Value - 1) - 1e-8
        && Btts - BttsAndOver25 <= TwoToThreeGoals + 1e-8
        && ExpectedGoals + .05 >= Math.Max(2 * Btts.Value, 3 * Over25.Value);

    public static bool Valid(double? value) => value.HasValue && double.IsFinite(value.Value) && value is >= 0 and <= 1;

    public static CombinedMarkets From(PoissonProbabilities value) => new()
    {
        HomeWin = value.HomeWin, Draw = value.Draw, AwayWin = value.AwayWin,
        Btts = value.BothTeamScoredGoal, Over25 = value.Over25, TwoToThreeGoals = value.TwoToThreeGoals,
        BttsAndOver25 = value.BttsAndOver25, ExpectedGoals = value.HomeExpectedGoals + value.AwayExpectedGoals
    };

    public WeightedPrediction ToPrediction()
    {
        if (!HasOutcomes || !HasGoals) throw new InvalidOperationException("Incomplete combined probabilities.");
        var winner = Draw >= HomeWin && Draw >= AwayWin ? "draw" : AwayWin > HomeWin ? "away" : "home";
        return new WeightedPrediction
        {
            HomeProb = HomeWin!.Value, DrawProb = Draw!.Value, AwayProb = AwayWin!.Value,
            BTTSProb = Btts!.Value, BTTS = Btts > .5, Over25Prob = Over25!.Value, Over25 = Over25 > .5,
            TwoToThreeGoalsProb = TwoToThreeGoals!.Value, TwoToThreeGoals = TwoToThreeGoals > .5,
            MatchWinner = winner, Confidence = Math.Max(HomeWin.Value, Math.Max(Draw.Value, AwayWin.Value))
        };
    }
}

public sealed record PredictionSource(string Name, string Method, CombinedMarkets? Markets = null,
    string? UnavailableReason = null, string? ModelVersion = null, string? Rationale = null,
    DateTimeOffset? CapturedAtUtc = null, double OutcomeWeight = 0, double GoalsWeight = 0);

public sealed record CombinedPrediction(int FixtureId, DateTimeOffset KickoffUtc, DateTimeOffset CapturedAtUtc,
    CombinedMarkets Markets, IReadOnlyList<PredictionSource> Sources)
{
    public string Version { get; init; } = "four-source-experimental-v1";
    public string WeightStatus { get; init; } = "configured_not_backtest_validated";
    public int? HomeTeamId { get; init; }
    public int? AwayTeamId { get; init; }
    public bool AllFourSourcesAvailable => Sources.Count == 4
        && Sources.All(source => source.OutcomeWeight > 0 && source.GoalsWeight > 0);
    public string Status => AllFourSourcesAvailable ? "complete" : "partial";
}

public sealed record CombinedPredictionRefreshResult(int FixtureId, Guid SnapshotId, CombinedPrediction Prediction,
    bool NarrationRefreshed, IReadOnlyList<string> Warnings);
