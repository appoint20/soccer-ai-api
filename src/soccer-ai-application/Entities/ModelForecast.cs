namespace SoccerAi.Application.Entities;

/// <summary>
/// A head-to-head record: what one language model forecast about a fixture's
/// goals, what the statistical pipeline forecast for the same fixture at the
/// same moment, and what actually happened.
///
/// One row per fixture per model, so several models can be scored against the
/// pipeline and against each other on identical inputs.
///
/// Both forecasts are frozen at prediction time. The pipeline recalibrates
/// continuously, so scoring a stored model forecast against a freshly
/// recomputed probability would compare an old forecast with a model that has
/// since seen more results — the pipeline would win on bookkeeping rather than
/// on skill.
///
/// A goals forecast carries no language, so unlike <see cref="FixtureAnalysis"/>
/// this is not duplicated per locale.
/// </summary>
public class ModelForecast
{
    public int Id { get; set; }

    public int FixtureId { get; set; }

    /// <summary>Model slug the forecast is attributed to, e.g. "anthropic/claude-sonnet-5".</summary>
    public string Model { get; set; } = "";

    public DateTimeOffset PredictedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Kickoff at prediction time — lets a query separate pre-match from in-play forecasts.</summary>
    public DateTimeOffset KickoffUtc { get; set; }

    // ── The model's forecast ──────────────────────────────────────
    public double ExpectedGoals { get; set; }
    public int PredictedHomeGoals { get; set; }
    public int PredictedAwayGoals { get; set; }
    public double Over25Probability { get; set; }
    public double BttsProbability { get; set; }

    /// <summary>The model's own stated confidence, 0-1. Recorded but never used for selection.</summary>
    public double Confidence { get; set; }

    public string Rationale { get; set; } = "";

    // ── The pipeline's forecast, frozen at the same instant ───────
    public double SystemExpectedGoals { get; set; }
    public double SystemOver25Probability { get; set; }
    public double SystemBttsProbability { get; set; }

    // ── Settlement ────────────────────────────────────────────────
    public DateTimeOffset? SettledAtUtc { get; set; }
    public int? ActualHomeGoals { get; set; }
    public int? ActualAwayGoals { get; set; }

    public int? ActualTotalGoals => ActualHomeGoals + ActualAwayGoals;
    public bool? ActualOver25 => ActualTotalGoals is { } total ? total > 2 : null;
    public bool? ActualBtts => ActualHomeGoals is { } h && ActualAwayGoals is { } a ? h > 0 && a > 0 : null;

    public bool IsSettled => SettledAtUtc.HasValue;
}
