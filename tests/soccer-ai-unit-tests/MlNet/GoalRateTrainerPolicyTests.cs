using FluentAssertions;
using SoccerAi.Infrastructure.MlNet;

namespace soccer_ai_unit_tests.MlNet;

public class GoalRateTrainerPolicyTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void MissingNativeDependencyCannotSilentlySelectAnotherProductionAlgorithm(bool publish, bool allowFallback)
    {
        var policy = new GoalRateTrainerPolicy(publish, allowFallback);
        var fallbackFits = 0;
        Action fit = () => policy.Fit<int>(() => throw new DllNotFoundException("lib_lightgbm"),
            () => ++fallbackFits, _ => { });
        fit.Should().Throw<InvalidOperationException>().WithMessage("*no replacement model was published*");
        fallbackFits.Should().Be(0);
        policy.UsedFallback.Should().BeFalse();
    }

    [Fact]
    public void ExplicitOfflineFallbackWarnsOnceAndDoesNotRetryBrokenNativeLibraryOnEveryFold()
    {
        var policy = new GoalRateTrainerPolicy(publish: false, allowOfflineFallback: true);
        var nativeFits = 0; var fallbackFits = 0; var warnings = 0;
        int Fit() => policy.Fit<int>(
            () => { nativeFits++; throw new TypeInitializationException("LightGbm", new DllNotFoundException()); },
            () => ++fallbackFits, _ => warnings++);
        Fit().Should().Be(1);
        Fit().Should().Be(2);
        nativeFits.Should().Be(1); warnings.Should().Be(1);
        policy.UsedFallback.Should().BeTrue();
        policy.TrainerUsed.Should().Be("FastTreeTweedie");
    }

    [Fact]
    public void UnrelatedTrainingErrorsAreNeverDisguisedAsANativeDependencyProblem()
    {
        var policy = new GoalRateTrainerPolicy(publish: false, allowOfflineFallback: true);
        var failure = new TypeInitializationException("OtherType", new InvalidOperationException("bad configuration"));
        Action fit = () => policy.Fit<int>(() => throw failure, () => 2, _ => { });
        fit.Should().Throw<TypeInitializationException>().Which.Should().BeSameAs(failure);
        policy.UsedFallback.Should().BeFalse();
    }

    [Fact]
    public void ReportCannotClaimPureLightGbmIfTheTrainerChangesMidAudit()
    {
        var policy = new GoalRateTrainerPolicy(publish: false, allowOfflineFallback: true);
        policy.Fit(() => 1, () => 2, _ => { }).Should().Be(1);
        policy.Fit<int>(() => throw new EntryPointNotFoundException(), () => 2, _ => { }).Should().Be(2);
        policy.TrainerUsed.Should().Be("LightGbm+FastTreeTweedie");
        policy.UsedFallback.Should().BeTrue();
    }
}
