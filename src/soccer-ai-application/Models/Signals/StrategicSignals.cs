using System.Text.Json.Serialization;

namespace SoccerAi.Application.Models.Signals;

/// <summary>
/// One discrete signal: numeric value + boolean flag + human-readable label.
/// Facts, not opinions — signals gate decisions, they never touch probabilities.
/// </summary>
public sealed record SignalValue(
    [property: JsonPropertyName("value")] double Value,
    [property: JsonPropertyName("flag")] bool Flag,
    [property: JsonPropertyName("label")] string Label)
{
    public static SignalValue Of(double value, bool flag, string label) =>
        new(Math.Round(value, 4), flag, label);

    public static SignalValue Unavailable(string reason) => new(0, false, reason);
}

/// <summary>A. Scoring &amp; conceding patterns for one side (venue-split + overall).</summary>
public sealed record ScoringSignals
{
    [JsonPropertyName("scored_in_last3_venue")] public SignalValue ScoredInLast3Venue { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("scored_in_last5_venue")] public SignalValue ScoredInLast5Venue { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("scored_in_last3_overall")] public SignalValue ScoredInLast3Overall { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("scored_in_last5_overall")] public SignalValue ScoredInLast5Overall { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("conceded_in_last3_venue")] public SignalValue ConcededInLast3Venue { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("conceded_in_last5_venue")] public SignalValue ConcededInLast5Venue { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("conceded_in_last3_overall")] public SignalValue ConcededInLast3Overall { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("conceded_in_last5_overall")] public SignalValue ConcededInLast5Overall { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("failed_to_score_last5_venue")] public SignalValue FailedToScoreLast5Venue { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("clean_sheets_last5_venue")] public SignalValue CleanSheetsLast5Venue { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("attack_trend")] public SignalValue AttackTrend { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("defense_trend")] public SignalValue DefenseTrend { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("over25_rate_last5_venue")] public SignalValue Over25RateLast5Venue { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("over25_rate_last10_venue")] public SignalValue Over25RateLast10Venue { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("btts_rate_last5_venue")] public SignalValue BttsRateLast5Venue { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("btts_rate_last10_venue")] public SignalValue BttsRateLast10Venue { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("under25_rate_last5_venue")] public SignalValue Under25RateLast5Venue { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("under25_rate_last10_venue")] public SignalValue Under25RateLast10Venue { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("first_half_goal_share")] public SignalValue FirstHalfGoalShare { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("second_half_goal_share")] public SignalValue SecondHalfGoalShare { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("avg_total_goals_last5")] public SignalValue AvgTotalGoalsLast5 { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("scoring_drought")] public SignalValue ScoringDrought { get; init; } = SignalValue.Unavailable("n/a");
}

/// <summary>B. Results &amp; form for one side.</summary>
public sealed record FormSignals
{
    [JsonPropertyName("form_last5")] public SignalValue FormLast5 { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("form_delta")] public SignalValue FormDelta { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("ppg_last5_venue")] public SignalValue PpgLast5Venue { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("season_ppg")] public SignalValue SeasonPpg { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("winless_streak")] public SignalValue WinlessStreak { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("unbeaten_streak")] public SignalValue UnbeatenStreak { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("losing_streak")] public SignalValue LosingStreak { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("points_from_losing_positions")] public SignalValue PointsFromLosingPositions { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("points_dropped_from_winning")] public SignalValue PointsDroppedFromWinning { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("tight_game_share_last10")] public SignalValue TightGameShareLast10 { get; init; } = SignalValue.Unavailable("n/a");
}

/// <summary>C. Table context (both sides).</summary>
public sealed record TableContextSignals
{
    [JsonPropertyName("home_rank")] public SignalValue HomeRank { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("away_rank")] public SignalValue AwayRank { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("rank_gap")] public SignalValue RankGap { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("ppg_gap")] public SignalValue PpgGap { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("goal_difference_gap")] public SignalValue GoalDifferenceGap { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("home_title_race")] public SignalValue HomeTitleRace { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("away_title_race")] public SignalValue AwayTitleRace { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("home_european_spots")] public SignalValue HomeEuropeanSpots { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("away_european_spots")] public SignalValue AwayEuropeanSpots { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("home_playoff_zone")] public SignalValue HomePlayoffZone { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("away_playoff_zone")] public SignalValue AwayPlayoffZone { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("home_relegation_zone")] public SignalValue HomeRelegationZone { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("away_relegation_zone")] public SignalValue AwayRelegationZone { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("home_dead_rubber")] public SignalValue HomeDeadRubber { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("away_dead_rubber")] public SignalValue AwayDeadRubber { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("motivation_asymmetry")] public SignalValue MotivationAsymmetry { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("season_phase")] public SignalValue SeasonPhase { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("run_in_with_stakes")] public SignalValue RunInWithStakes { get; init; } = SignalValue.Unavailable("n/a");
}

/// <summary>D. Head-to-head signals (same pairing).</summary>
public sealed record HeadToHeadSignals
{
    [JsonPropertyName("btts_rate_last5")] public SignalValue BttsRateLast5 { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("btts_rate_last10")] public SignalValue BttsRateLast10 { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("draw_rate_last5")] public SignalValue DrawRateLast5 { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("over25_rate_last5")] public SignalValue Over25RateLast5 { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("over25_rate_last10")] public SignalValue Over25RateLast10 { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("avg_total_goals")] public SignalValue AvgTotalGoals { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("avg_goal_margin")] public SignalValue AvgGoalMargin { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("home_venue_home_win_rate")] public SignalValue HomeVenueHomeWinRate { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("home_venue_avg_goals")] public SignalValue HomeVenueAvgGoals { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("dominance")] public SignalValue Dominance { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("style_clash")] public SignalValue StyleClash { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("derby")] public SignalValue Derby { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("sample_size")] public int SampleSize { get; init; }
}

/// <summary>E. Schedule &amp; fatigue.</summary>
public sealed record ScheduleSignals
{
    [JsonPropertyName("home_rest_days")] public SignalValue HomeRestDays { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("away_rest_days")] public SignalValue AwayRestDays { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("rest_day_gap")] public SignalValue RestDayGap { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("home_matches_last14d")] public SignalValue HomeMatchesLast14Days { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("away_matches_last14d")] public SignalValue AwayMatchesLast14Days { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("home_tier2_within4d")] public SignalValue HomeTier2Within4Days { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("away_tier2_within4d")] public SignalValue AwayTier2Within4Days { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("travel")] public SignalValue Travel { get; init; } = SignalValue.Unavailable("Travel distance not computed (no venue geodata)");
}

/// <summary>F. Squad availability — degrades gracefully when not synced.</summary>
public sealed record AvailabilitySignals
{
    [JsonPropertyName("data_available")] public bool DataAvailable { get; init; }
    [JsonPropertyName("home_key_absences")] public SignalValue HomeKeyAbsences { get; init; } = SignalValue.Unavailable("No availability data synced");
    [JsonPropertyName("away_key_absences")] public SignalValue AwayKeyAbsences { get; init; } = SignalValue.Unavailable("No availability data synced");
    [JsonPropertyName("home_top_scorer_available")] public SignalValue HomeTopScorerAvailable { get; init; } = SignalValue.Unavailable("No availability data synced");
    [JsonPropertyName("away_top_scorer_available")] public SignalValue AwayTopScorerAvailable { get; init; } = SignalValue.Unavailable("No availability data synced");
}

/// <summary>G. Market signals from stored odds.</summary>
public sealed record MarketSignals
{
    [JsonPropertyName("opening_drift")] public SignalValue OpeningDrift { get; init; } = SignalValue.Unavailable("Opening odds not stored — drift unavailable");
    [JsonPropertyName("divergence_over25")] public SignalValue DivergenceOver25 { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("divergence_btts")] public SignalValue DivergenceBtts { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("divergence_1x2")] public SignalValue Divergence1X2 { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("favorite_odds_band")] public SignalValue FavoriteOddsBand { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("trap")] public SignalValue Trap { get; init; } = SignalValue.Unavailable("n/a");
}

/// <summary>H. League profile base rates.</summary>
public sealed record LeagueProfileSignals
{
    [JsonPropertyName("league_over25_rate")] public SignalValue LeagueOver25Rate { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("league_btts_rate")] public SignalValue LeagueBttsRate { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("league_volatility")] public SignalValue LeagueVolatility { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("home_over25_vs_league")] public SignalValue HomeOver25VsLeague { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("away_over25_vs_league")] public SignalValue AwayOver25VsLeague { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("home_btts_vs_league")] public SignalValue HomeBttsVsLeague { get; init; } = SignalValue.Unavailable("n/a");
    [JsonPropertyName("away_btts_vs_league")] public SignalValue AwayBttsVsLeague { get; init; } = SignalValue.Unavailable("n/a");
}

/// <summary>
/// The full strategic signal catalog for one fixture, computed strictly from
/// pre-kickoff data. Persisted inside the analysis snapshot.
/// </summary>
public sealed record StrategicSignals
{
    [JsonPropertyName("home_scoring")] public ScoringSignals HomeScoring { get; init; } = new();
    [JsonPropertyName("away_scoring")] public ScoringSignals AwayScoring { get; init; } = new();
    [JsonPropertyName("home_form")] public FormSignals HomeForm { get; init; } = new();
    [JsonPropertyName("away_form")] public FormSignals AwayForm { get; init; } = new();
    [JsonPropertyName("table")] public TableContextSignals Table { get; init; } = new();
    [JsonPropertyName("h2h")] public HeadToHeadSignals H2H { get; init; } = new();
    [JsonPropertyName("schedule")] public ScheduleSignals Schedule { get; init; } = new();
    [JsonPropertyName("availability")] public AvailabilitySignals Availability { get; init; } = new();
    [JsonPropertyName("market")] public MarketSignals Market { get; init; } = new();
    [JsonPropertyName("league")] public LeagueProfileSignals League { get; init; } = new();
    [JsonPropertyName("computed_at_utc")] public DateTimeOffset ComputedAtUtc { get; init; }
}
