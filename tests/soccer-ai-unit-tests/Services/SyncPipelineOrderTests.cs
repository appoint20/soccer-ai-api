using FluentAssertions;
using System.Linq;
using SoccerAi.Application.Services.Sync;

namespace soccer_ai_unit_tests.Services;

/// <summary>
/// Guards the pipeline's composition rather than its behaviour.
///
/// A step can be fully implemented, registered and wired and still never run,
/// simply by not appearing in the execution order. That is not hypothetical:
/// goal-rate training shipped complete and sat unreachable because the worker's
/// pipeline never listed it, so the model silently never retrained in
/// production and every forecast fell back to Dixon-Coles.
/// </summary>
public class SyncPipelineOrderTests
{
    [Fact]
    public void TrainingIsPartOfThePipeline()
    {
        SyncPipeline.ExecutionOrder.Should().Contain(SyncPipeline.Steps.TrainModel);
    }

    [Fact]
    public void TrainingRunsAfterTheFixturesItLearnsFrom()
    {
        var order = SyncPipeline.ExecutionOrder.ToList();

        order.IndexOf(SyncPipeline.Steps.TrainModel)
            .Should().BeGreaterThan(order.IndexOf(SyncPipeline.Steps.FixturesAndOdds),
                "training on results the sync has not fetched yet learns from stale data");
    }

    [Fact]
    public void TrainingRunsBeforeTheRecomputeThatServesIt()
    {
        var order = SyncPipeline.ExecutionOrder.ToList();

        order.IndexOf(SyncPipeline.Steps.TrainModel)
            .Should().BeLessThan(order.IndexOf(SyncPipeline.Steps.RecomputeAnalysis),
                "recomputing first would publish a board built by the previous "
                + "generation, leaving the served model permanently one run behind");
    }

    [Fact]
    public void EveryStepAppearsExactlyOnce()
    {
        // A duplicated step would run twice and, worse, break the resume logic,
        // which locates progress by the first index of the last completed step.
        SyncPipeline.ExecutionOrder.Should().OnlyHaveUniqueItems();
    }
}
