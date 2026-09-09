namespace SoccerAi.Application.Options;

/// <summary>
/// Configuration for the hybrid ML + Dixon-Coles goal-rate model
/// ("HybridModel" section).
/// </summary>
public sealed class HybridModelOptions
{
    public const string SectionName = "HybridModel";

    /// <summary>
    /// Master switch. When off, the probability pipeline runs classic
    /// Dixon-Coles exactly as before, so this can be turned off without a
    /// deploy if the learned model misbehaves.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Directory holding the trained model files, relative to the content root.</summary>
    public string ModelDirectory { get; set; } = Path.Combine("data", "models");

    public string HomeModelFile { get; set; } = "goal_rate_home.zip";
    public string AwayModelFile { get; set; } = "goal_rate_away.zip";

    /// <summary>
    /// Multiplicative λ corrections fitted at training time and applied at
    /// serving time.
    /// </summary>
    /// <remarks>
    /// Fitted on a chronological block excluded from tree fitting. The factor
    /// is part of the immutable generation and must be loaded with its models.
    /// </remarks>
    public string CalibrationFile { get; set; } = "goal_rate_calibration.json";

    /// <summary>
    /// Clamps on the learned goal rate. A tree ensemble extrapolates poorly at
    /// the edges of its training distribution, and an unclamped λ feeds a score
    /// matrix that then reports near-certainties.
    /// </summary>
    public double LambdaMin { get; set; } = 0.15;
    public double LambdaMax { get; set; } = 4.5;

    // Publish floors are NOT defined here. The gate that decides what reaches a
    // customer is ConfluenceOptions.ConfidencePickMinProbabilityByMarket, read
    // through PickSelector.ConfidenceFloorFor, and the training report measures
    // those same values — so the numbers in goal_rate_evaluation.json describe
    // the board that will actually be published, not a parallel setting that
    // could quietly drift away from it.

    /// <summary>
    /// Minimum finished fixtures before training will run at all. Below this
    /// the learned rates are worse than the shrinkage-based ones they replace.
    /// </summary>
    public int MinTrainingRows { get; set; } = 3000;

    /// <summary>
    /// Minimum age of the published generation before a training run will
    /// replace it. 0 retrains on every call.
    /// </summary>
    /// <remarks>
    /// The sync runs eight times a day; training does not need to. A day of new
    /// results moves the fitted rates barely at all, while a full run rebuilds
    /// features for every finished fixture and fits eighteen models — the most
    /// expensive thing in the pipeline by a wide margin, and the most likely to
    /// exhaust memory on a small instance.
    ///
    /// The gate lives here rather than in the scheduler on purpose: it is a
    /// property of the artifact's age, so it survives a missed slot, a resumed
    /// run, or a restart, none of which a "train at 03:00" rule would.
    /// </remarks>
    public double RetrainIntervalHours { get; set; } = 24;

    /// <summary>
    /// Share of the oldest rows used only to warm up walk-forward evaluation;
    /// nothing before this point is ever scored.
    /// </summary>
    public double WalkForwardWarmupFraction { get; set; } = 0.40;

    /// <summary>
    /// Folds in the walk-forward evaluation. Each fold refits both models, so
    /// this is the main cost of a training run — 8 folds is about a minute.
    /// </summary>
    public int WalkForwardFolds { get; set; } = 8;

    /// <summary>
    /// Share used to fit the λ corrections. Taken from the block immediately
    /// after training, so the correction is measured on unseen data but still
    /// ahead of the evaluation block — the evaluation stays a clean held-out
    /// test of the fully assembled model.
    /// </summary>
    public double ValidationFraction { get; set; } = 0.15;

    // ── LightGBM hyper-parameters ──
    // Existing defaults are retained. Any tuning requires a new untouched
    // chronological evaluation period; threshold sweeps are descriptive only.

    /// <summary>Boosting iterations per goal-rate model.</summary>
    public int Trees { get; set; } = 700;
    public int Leaves { get; set; } = 31;
    public int MinExamplesPerLeaf { get; set; } = 100;
    public double LearningRate { get; set; } = 0.02;
    public double L2Regularization { get; set; } = 5.0;
    public double FeatureFraction { get; set; } = 0.7;
    public double SubsampleFraction { get; set; } = 0.8;
}
