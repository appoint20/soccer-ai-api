namespace SoccerAi.Application.Entities;

/// <summary>
/// The provider's latest prediction for one fixture.
/// </summary>
/// <remarks>
/// Replaced in place rather than appended: this is a second opinion shown
/// beside our own, and only the current one is ever read. The columns mirror
/// <see cref="Models.ProviderPrediction"/>; every comparison figure is the HOME
/// side's share of it, so the away side is one minus the value.
///
/// None of it comes from a bookmaker, which is the point — it is the evidence a
/// match panel can stand on when no price exists, and most fixtures more than a
/// day out have no usable price.
/// </remarks>
public class FixturePrediction
{
    public int Id { get; set; }

    public int FixtureId { get; set; }

    public double? PercentHome { get; set; }

    public double? PercentDraw { get; set; }

    public double? PercentAway { get; set; }

    public double? Form { get; set; }

    public double? Attack { get; set; }

    public double? Defence { get; set; }

    public double? Poisson { get; set; }

    public double? HeadToHead { get; set; }

    public double? Goals { get; set; }

    public double? Total { get; set; }

    public string? Advice { get; set; }

    public string? WinnerName { get; set; }

    public string? UnderOver { get; set; }

    public int HomePlayed { get; set; }

    public double? HomeForm { get; set; }

    public double? HomeAttack { get; set; }

    public double? HomeDefence { get; set; }

    public double? HomeGoalsFor { get; set; }

    public double? HomeGoalsAgainst { get; set; }

    public int AwayPlayed { get; set; }

    public double? AwayForm { get; set; }

    public double? AwayAttack { get; set; }

    public double? AwayDefence { get; set; }

    public double? AwayGoalsFor { get; set; }

    public double? AwayGoalsAgainst { get; set; }

    public DateTimeOffset CapturedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
