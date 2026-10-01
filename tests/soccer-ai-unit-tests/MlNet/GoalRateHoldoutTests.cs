using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SoccerAi.Application.Entities;
using SoccerAi.Application.Options;
using SoccerAi.Infrastructure.MlNet;

namespace soccer_ai_unit_tests.MlNet;

public class GoalRateHoldoutTests
{
    [Fact]
    public void HoldoutLabelsCannotChangeTheFittedModelOrTheirOwnForecast()
    {
        var start = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var fixtures = Enumerable.Range(0, 100).Select(index => new Fixture
        {
            Id = index + 1, Date = start.AddDays(index), Status = "FT", LeagueId = 39,
            HomeTeamId = index % 2 + 1, AwayTeamId = 2 - index % 2,
            HomeGoal = index % 4, AwayGoal = index % 3
        }).ToList();
        var cutoff = start.AddDays(90).Date;
        using var services = new ServiceCollection().BuildServiceProvider();
        var settings = new HybridModelOptions
        {
            AllowOfflineTrainerFallback = true, MinTrainingRows = 20, Trees = 4,
            Leaves = 4, MinExamplesPerLeaf = 2
        };
        var builder = new GoalRateFeatureBuilder(Options.Create(new DixonColesOptions()),
            NullLogger<GoalRateFeatureBuilder>.Instance);
        GoalRateTrainingService Trainer() => new(NullLogger<GoalRateTrainingService>.Instance,
            builder, Options.Create(settings), Options.Create(new ConfluenceOptions()),
            services.GetRequiredService<IServiceScopeFactory>());
        var before = Trainer().ForecastHoldout(fixtures, cutoff, start.AddDays(100));
        foreach (var fixture in fixtures.Where(fixture => fixture.Date >= cutoff))
        {
            fixture.HomeGoal = 10;
            fixture.AwayGoal = 10;
        }
        var after = Trainer().ForecastHoldout(fixtures, cutoff, start.AddDays(100));
        before.TrainingThroughUtc.Should().BeBefore(before.CalibrationFromUtc);
        before.CalibrationThroughUtc.Should().BeBefore(cutoff);
        before.TrainingRows.Should().Be(after.TrainingRows);
        before.Calibration.MlWeight.Should().Be(after.Calibration.MlWeight);
        before.Calibration.HomeScale.Should().Be(after.Calibration.HomeScale);
        before.Predictions[0].Ml.Should().Be(after.Predictions[0].Ml);
        before.Predictions[0].Historical.Should().Be(after.Predictions[0].Historical);
        before.Predictions.Should().HaveCount(10);
    }
}
