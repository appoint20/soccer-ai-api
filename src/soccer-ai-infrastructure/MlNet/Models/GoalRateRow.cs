namespace SoccerAi.Infrastructure.MlNet.Models;

/// <summary>
/// One row per fixture for the goal-rate models. Every feature is computed
/// STRICTLY from data available before kickoff; the two labels are the actual
/// goals each side scored.
///
/// Two models are trained from this single row — one predicting
/// <see cref="GoalsHome"/>, one predicting <see cref="GoalsAway"/> — and their
/// outputs become the λ pair fed to the Dixon-Coles score matrix. Deriving
/// every market from one matrix keeps BTTS, Over 2.5 and 1X2 mutually
/// consistent, which per-market binary classifiers cannot guarantee.
/// </summary>
public sealed class GoalRateRow
{
    // ── Metadata: excluded from training ────────────────────────────────────
    public float FixtureId { get; set; }
    public float LeagueId { get; set; }
    public DateTime Date { get; set; }

    /// <summary>
    /// True when the fixture has been played and <see cref="GoalsHome"/> /
    /// <see cref="GoalsAway"/> are real results. Rows built for upcoming
    /// fixtures carry features only and must never enter training.
    /// </summary>
    public bool IsFinished { get; set; }

    // ── Classic Dixon-Coles view (the current production model) ─────────────
    public float DcOver25 { get; set; }
    public float DcBtts { get; set; }
    public float DcHome { get; set; }
    public float DcDraw { get; set; }
    public float DcAway { get; set; }
    public float DcLambdaHome { get; set; }
    public float DcLambdaAway { get; set; }
    public float DcLambdaSum { get; set; }
    public float DcLambdaMin { get; set; }

    // ── Bookmaker view ──────────────────────────────────────────────────────
    public float MktOver25 { get; set; }
    public float MktBtts { get; set; }
    public float MktFavourite { get; set; }
    public float HasMktOver { get; set; }
    public float HasMktBtts { get; set; }

    /// <summary>Goal rates recovered from 1X2 + O/U by <c>MarketLambdaSolver</c>.</summary>
    public float MktLambdaHome { get; set; }
    public float MktLambdaAway { get; set; }
    public float MktLambdaSum { get; set; }
    public float MktLambdaMin { get; set; }
    public float MktBttsImplied { get; set; }
    public float MktOverImplied { get; set; }
    public float HasMktLambda { get; set; }

    // ── Where the model and the market disagree (the exploitable residual) ──
    public float DivOver { get; set; }
    public float DivBtts { get; set; }
    public float DivLambdaSum { get; set; }
    public float DivBttsImplied { get; set; }

    // ── Rolling match quality, last 10 (the signal production ignores) ──────
    public float HomeXgFor { get; set; }
    public float HomeXgAgainst { get; set; }
    public float AwayXgFor { get; set; }
    public float AwayXgAgainst { get; set; }
    public float HomeXgSamples { get; set; }
    public float AwayXgSamples { get; set; }
    public float HomeXgAgainstSamples { get; set; }
    public float AwayXgAgainstSamples { get; set; }
    public float HomeSotFor { get; set; }
    public float HomeSotAgainst { get; set; }
    public float AwaySotFor { get; set; }
    public float AwaySotAgainst { get; set; }
    public float HomeShotsFor { get; set; }
    public float AwayShotsFor { get; set; }
    public float HomeGoalsFor { get; set; }
    public float HomeGoalsAgainst { get; set; }
    public float AwayGoalsFor { get; set; }
    public float AwayGoalsAgainst { get; set; }
    public float HomePossession { get; set; }
    public float AwayPossession { get; set; }
    public float HomeBttsRate { get; set; }
    public float AwayBttsRate { get; set; }
    public float HomeOver25Rate { get; set; }
    public float AwayOver25Rate { get; set; }
    public float HomeMatchTotal { get; set; }
    public float AwayMatchTotal { get; set; }
    public float HomeHalfTimeTotal { get; set; }
    public float AwayHalfTimeTotal { get; set; }

    // ── Rolling, last 5 — picks up form the 10-match window smooths away ────
    public float HomeGoalsForShort { get; set; }
    public float HomeGoalsAgainstShort { get; set; }
    public float AwayGoalsForShort { get; set; }
    public float AwayGoalsAgainstShort { get; set; }
    public float HomeXgForShort { get; set; }
    public float AwayXgForShort { get; set; }

    // ── Venue-specific (home side at home, away side away) ──────────────────
    public float HomeVenueXgFor { get; set; }
    public float HomeVenueXgAgainst { get; set; }
    public float AwayVenueXgFor { get; set; }
    public float AwayVenueXgAgainst { get; set; }
    public float HomeVenueGoalsFor { get; set; }
    public float HomeVenueGoalsAgainst { get; set; }
    public float AwayVenueGoalsFor { get; set; }
    public float AwayVenueGoalsAgainst { get; set; }
    public float HomeVenueBtts { get; set; }
    public float AwayVenueBtts { get; set; }
    public float HomeVenueOver25 { get; set; }
    public float AwayVenueOver25 { get; set; }

    // ── Head to head ────────────────────────────────────────────────────────
    public float H2HBtts { get; set; }
    public float H2HOver25 { get; set; }
    public float H2HTotal { get; set; }
    public float H2HSample { get; set; }

    // ── Context ─────────────────────────────────────────────────────────────
    public float EloDiff { get; set; }
    public float HomeRestDays { get; set; }
    public float AwayRestDays { get; set; }
    public float IsDerby { get; set; }
    public float LeagueHomeAvg { get; set; }
    public float LeagueAwayAvg { get; set; }
    public float HomeHistory { get; set; }
    public float AwayHistory { get; set; }

    // ── Labels ──────────────────────────────────────────────────────────────
    public float GoalsHome { get; set; }
    public float GoalsAway { get; set; }

    /// <summary>Columns that are identifiers or labels, never inputs.</summary>
    public static readonly string[] NonFeatureColumns =
    [
        nameof(FixtureId), nameof(LeagueId), nameof(Date),
        nameof(GoalsHome), nameof(GoalsAway)
    ];

    /// <summary>Every column the trainer should assemble into the feature vector.</summary>
    public static string[] FeatureColumns() =>
        [.. typeof(GoalRateRow)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(float))
            .Select(p => p.Name)
            .Where(n => !NonFeatureColumns.Contains(n))];
}
