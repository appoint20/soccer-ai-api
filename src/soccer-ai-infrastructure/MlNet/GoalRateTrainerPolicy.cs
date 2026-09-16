namespace SoccerAi.Infrastructure.MlNet;

/// <summary>Keeps a missing native dependency from silently changing production training.</summary>
public sealed class GoalRateTrainerPolicy(bool publish, bool allowOfflineFallback)
{
    public const string ProductionTrainer = "LightGbm";
    public bool UsedFallback { get; private set; }
    private bool _usedLightGbm;
    public string TrainerUsed => UsedFallback
        ? (_usedLightGbm ? "LightGbm+FastTreeTweedie" : "FastTreeTweedie")
        : _usedLightGbm ? ProductionTrainer : "";

    public T Fit<T>(Func<T> nativeFit, Func<T> fallbackFit, Action<Exception> logFallback)
    {
        if (UsedFallback) return fallbackFit();
        try
        {
            var model = nativeFit();
            _usedLightGbm = true;
            return model;
        }
        catch (Exception ex) when (IsNativeDependencyFailure(ex))
        {
            if (publish || !allowOfflineFallback)
                throw new InvalidOperationException(
                    "LightGBM native training is unavailable. On the Linux image, install libgomp1 and verify "
                    + "the native library architecture with --check-lightgbm. This training run was stopped; "
                    + "no replacement model was published. The sync pipeline can continue with the existing model or Dixon-Coles.", ex);
            UsedFallback = true;
            logFallback(ex);
            return fallbackFit();
        }
    }

    private static bool IsNativeDependencyFailure(Exception ex) =>
        ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException ||
        ex is TypeInitializationException { InnerException: { } inner } && IsNativeDependencyFailure(inner);
}
