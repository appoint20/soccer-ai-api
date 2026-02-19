"""
Optimized feature extraction pipeline for football match prediction.
Uses vectorized operations for speed.
"""

import sqlite3
import pandas as pd
import numpy as np
from pathlib import Path

# Configuration
DB_PATH = Path(__file__).parent.parent.parent / "soccer.db"
OUTPUT_PATH = Path(__file__).parent / "training_data.parquet"
ROLLING_WINDOW = 5


def load_fixtures(db_path: Path) -> pd.DataFrame:
    """Load finished fixtures from SQLite database."""
    conn = sqlite3.connect(db_path)
    
    query = """
    SELECT 
        Id, ApiId, HomeTeamId, AwayTeamId, LeagueId, Date, Status,
        HomeGoal, AwayGoal, HtHomeGoal, HtAwayGoal,
        HomeGoalAvg, AwayGoalAvg, HtHomeGoalAvg, HtAwayGoalAvg,
        HomeShots, AwayShots, HomeShotsOnTarget, AwayShotsOnTarget,
        HomeBallPossession, AwayBallPossession,
        HomeXg, AwayXg,
        HomeWinOdds, DrawOdds, AwayWinOdds, Over25Odds, Under25Odds, BttsYesOdds,
        IsDerby, IsCurrentSeason
    FROM Fixtures
    WHERE Status = 'FT'
    ORDER BY Date ASC
    """
    
    df = pd.read_sql_query(query, conn)
    conn.close()
    
    df['Date'] = pd.to_datetime(df['Date'])
    print(f"Loaded {len(df)} finished fixtures")
    return df


def calculate_rolling_features(df: pd.DataFrame) -> pd.DataFrame:
    """Calculate rolling features using vectorized operations."""
    print("Calculating rolling features...")
    
    # Calculate derived columns first
    df['TotalGoals'] = df['HomeGoal'] + df['AwayGoal']
    df['BTTS'] = ((df['HomeGoal'] > 0) & (df['AwayGoal'] > 0)).astype(int)
    df['Over25'] = (df['TotalGoals'] > 2.5).astype(int)
    df['HomeCleanSheet'] = (df['AwayGoal'] == 0).astype(int)
    df['AwayCleanSheet'] = (df['HomeGoal'] == 0).astype(int)
    df['HomeFailedToScore'] = (df['HomeGoal'] == 0).astype(int)
    df['AwayFailedToScore'] = (df['AwayGoal'] == 0).astype(int)
    
    # Home team perspective (grouped by HomeTeamId)
    home_groups = df.groupby('HomeTeamId')
    
    # Rolling averages for home team (when playing at home)
    df['home_goals_scored_avg'] = home_groups['HomeGoal'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    df['home_goals_conceded_avg'] = home_groups['AwayGoal'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    df['home_xg_avg'] = home_groups['HomeXg'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    df['home_shots_avg'] = home_groups['HomeShots'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    df['home_shots_on_target_avg'] = home_groups['HomeShotsOnTarget'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    df['home_btts_rate'] = home_groups['BTTS'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    df['home_over25_rate'] = home_groups['Over25'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    df['home_clean_sheet_rate'] = home_groups['HomeCleanSheet'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    df['home_failed_to_score_rate'] = home_groups['HomeFailedToScore'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    
    # Away team perspective (grouped by AwayTeamId)
    away_groups = df.groupby('AwayTeamId')
    
    df['away_goals_scored_avg'] = away_groups['AwayGoal'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    df['away_goals_conceded_avg'] = away_groups['HomeGoal'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    df['away_xg_avg'] = away_groups['AwayXg'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    df['away_shots_avg'] = away_groups['AwayShots'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    df['away_shots_on_target_avg'] = away_groups['AwayShotsOnTarget'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    df['away_btts_rate'] = away_groups['BTTS'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    df['away_over25_rate'] = away_groups['Over25'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    df['away_clean_sheet_rate'] = away_groups['AwayCleanSheet'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    df['away_failed_to_score_rate'] = away_groups['AwayFailedToScore'].transform(
        lambda x: x.shift(1).rolling(ROLLING_WINDOW, min_periods=1).mean()
    )
    
    # League-level features (grouped by LeagueId)
    league_groups = df.groupby('LeagueId')
    
    df['league_avg_goals'] = league_groups['TotalGoals'].transform(
        lambda x: x.shift(1).rolling(100, min_periods=10).mean()
    )
    df['league_btts_rate'] = league_groups['BTTS'].transform(
        lambda x: x.shift(1).rolling(100, min_periods=10).mean()
    )
    df['league_over25_rate'] = league_groups['Over25'].transform(
        lambda x: x.shift(1).rolling(100, min_periods=10).mean()
    )
    
    return df


def calculate_h2h_features(df: pd.DataFrame) -> pd.DataFrame:
    """Calculate head-to-head features."""
    print("Calculating H2H features...")
    
    # Create a unique H2H key (sorted team IDs to handle both home/away)
    df['h2h_key'] = df.apply(
        lambda x: tuple(sorted([x['HomeTeamId'], x['AwayTeamId']])), axis=1
    )
    df['h2h_key'] = df['h2h_key'].astype(str)
    
    # Group by H2H key and calculate rolling stats
    h2h_groups = df.groupby('h2h_key')
    
    df['h2h_total_goals_avg'] = h2h_groups['TotalGoals'].transform(
        lambda x: x.shift(1).rolling(5, min_periods=1).mean()
    )
    df['h2h_btts_rate'] = h2h_groups['BTTS'].transform(
        lambda x: x.shift(1).rolling(5, min_periods=1).mean()
    )
    df['h2h_over25_rate'] = h2h_groups['Over25'].transform(
        lambda x: x.shift(1).rolling(5, min_periods=1).mean()
    )
    
    # Drop temporary column
    df = df.drop(columns=['h2h_key'])
    
    return df


def calculate_overall_features(df: pd.DataFrame) -> pd.DataFrame:
    """Calculate overall form features (Home + Away combined)."""
    print("Calculating overall form features...")
    
    # Melting to team-match level for overall form
    home_matches = df[['Date', 'HomeTeamId', 'HomeGoal', 'AwayGoal', 'HomeXg', 'TotalGoals', 'BTTS', 'Over25']].copy()
    home_matches = home_matches.rename(columns={
        'HomeTeamId': 'TeamId', 'HomeGoal': 'Scored', 'AwayGoal': 'Conceded', 'HomeXg': 'Xg'
    })
    
    away_matches = df[['Date', 'AwayTeamId', 'AwayGoal', 'HomeGoal', 'AwayXg', 'TotalGoals', 'BTTS', 'Over25']].copy()
    away_matches = away_matches.rename(columns={
        'AwayTeamId': 'TeamId', 'AwayGoal': 'Scored', 'HomeGoal': 'Conceded', 'AwayXg': 'Xg'
    })
    
    team_matches = pd.concat([home_matches, away_matches]).sort_values(['TeamId', 'Date'])
    
    # 2. Calculate rolling stats per team
    team_groups = team_matches.groupby('TeamId')
    
    # Standard averages (Last 5)
    team_matches['overall_goals_scored_avg'] = team_groups['Scored'].transform(lambda x: x.shift(1).rolling(5, min_periods=1).mean())
    team_matches['overall_goals_conceded_avg'] = team_groups['Conceded'].transform(lambda x: x.shift(1).rolling(5, min_periods=1).mean())
    team_matches['overall_xg_avg'] = team_groups['Xg'].transform(lambda x: x.shift(1).rolling(5, min_periods=1).mean())
    team_matches['overall_btts_rate'] = team_groups['BTTS'].transform(lambda x: x.shift(1).rolling(5, min_periods=1).mean())
    team_matches['overall_over25_rate'] = team_groups['Over25'].transform(lambda x: x.shift(1).rolling(5, min_periods=1).mean())
    
    # Seasonal averages (Mean Reversion Base)
    team_matches['seasonal_scored_avg'] = team_groups['Scored'].transform(lambda x: x.shift(1).expanding(min_periods=5).mean())
    team_matches['seasonal_xg_avg'] = team_groups['Xg'].transform(lambda x: x.shift(1).expanding(min_periods=5).mean())
    
    # Mean Reversion Diffs (Recent - Seasonal)
    # If positive: Team is overperforming recent history. If negative: Underperforming.
    team_matches['overall_scored_diff'] = team_matches['overall_goals_scored_avg'] - team_matches['seasonal_scored_avg']
    team_matches['overall_xg_diff'] = team_matches['overall_xg_avg'] - team_matches['seasonal_xg_avg']
    
    # 3. Calculate Streaks
    def get_streak(series):
        # Shift to avoid leakage
        s = series.shift(1).fillna(0)
        # Group by blocks of identical values
        blocks = (s != s.shift(1)).cumsum()
        return s.groupby(blocks).cumcount() + 1
    
    # Under 2.5 Streak
    # Note: Only count if the value is 0 (Under). If it's 1 (Over), the streak is for Over.
    # We want two separate features: under_streak and over_streak.
    
    def calculate_discrete_streak(series, target_val):
        s = series.shift(1)
        # Mask: 1 if it matches target_val, else 0
        mask = (s == target_val).astype(int)
        # Groups of consecutive identical values in the mask
        groups = (mask != mask.shift(1)).cumsum()
        # Cumulative count within groups, then reset where mask is 0
        streaks = mask.groupby(groups).cumcount() + 1
        return streaks * mask

    team_matches['overall_under_streak'] = team_groups['Over25'].transform(lambda x: calculate_discrete_streak(x, 0))
    team_matches['overall_over_streak'] = team_groups['Over25'].transform(lambda x: calculate_discrete_streak(x, 1))
    team_matches['overall_btts_streak'] = team_groups['BTTS'].transform(lambda x: calculate_discrete_streak(x, 1))
    
    # 4. Join back to main fixture dataframe
    team_stats_cols = [
        'Date', 'TeamId', 
        'overall_goals_scored_avg', 'overall_goals_conceded_avg', 
        'overall_xg_avg', 'overall_btts_rate', 'overall_over25_rate',
        'overall_scored_diff', 'overall_xg_diff',
        'overall_under_streak', 'overall_over_streak', 'overall_btts_streak'
    ]
    team_stats = team_matches[team_stats_cols].drop_duplicates(['Date', 'TeamId'])
    
    # Join for Home Team
    df = df.merge(team_stats, left_on=['Date', 'HomeTeamId'], right_on=['Date', 'TeamId'], how='left')
    df = df.rename(columns={c: f'home_{c}' for c in team_stats_cols if c not in ['Date', 'TeamId']}).drop(columns=['TeamId'])
    
    # Join for Away Team
    df = df.merge(team_stats, left_on=['Date', 'AwayTeamId'], right_on=['Date', 'TeamId'], how='left')
    df = df.rename(columns={c: f'away_{c}' for c in team_stats_cols if c not in ['Date', 'TeamId']}).drop(columns=['TeamId'])
    
    return df


def calculate_temporal_features(df: pd.DataFrame) -> pd.DataFrame:
    """Calculate temporal and seasonality features."""
    print("Calculating temporal features...")
    
    # Day of week (0=Monday, 6=Sunday)
    df['day_of_week'] = df['Date'].dt.dayofweek
    
    # Is Weekend (Saturday=5, Sunday=6)
    df['is_weekend'] = df['day_of_week'].isin([5, 6]).astype(int)
    
    # Month (1-12)
    df['month'] = df['Date'].dt.month
    
    # Season Progress (approximate, assuming Aug-May season)
    # Aug=0.0, May=1.0. 
    # Logic: map month to an academic year offset.
    # 8(Aug)->0, 9->1, ..., 12->4, 1->5, ..., 5->9. 6,7 off-season.
    # Simple standardized month:
    df['season_month_idx'] = df['month'].apply(lambda m: (m - 8) if m >= 8 else (m + 4))
    
    return df


def prepare_training_data(df: pd.DataFrame) -> pd.DataFrame:
    """Prepare final training dataset."""
    print("Preparing final dataset...")
    
    # Fill NaN values with sensible defaults
    default_values = {
        'home_goals_scored_avg': 1.3,
        'home_goals_conceded_avg': 1.0,
        'home_xg_avg': 1.2,
        'home_shots_avg': 10.0,
        'home_shots_on_target_avg': 4.0,
        'home_btts_rate': 0.5,
        'home_over25_rate': 0.5,
        'home_clean_sheet_rate': 0.3,
        'home_failed_to_score_rate': 0.2,
        
        'away_goals_scored_avg': 1.0,
        'away_goals_conceded_avg': 1.3,
        'away_xg_avg': 1.0,
        'away_shots_avg': 9.0,
        'away_shots_on_target_avg': 3.5,
        'away_btts_rate': 0.5,
        'away_over25_rate': 0.5,
        'away_clean_sheet_rate': 0.25,
        'away_failed_to_score_rate': 0.25,
        
        'h2h_total_goals_avg': 2.5,
        'h2h_btts_rate': 0.5,
        'h2h_over25_rate': 0.5,
        
        'league_avg_goals': 2.5,
        'league_btts_rate': 0.5,
        'league_over25_rate': 0.5,
        
        # New Overall Defaults
        'home_overall_goals_scored_avg': 1.3,
        'home_overall_goals_conceded_avg': 1.1,
        'home_overall_xg_avg': 1.2,
        'home_overall_btts_rate': 0.5,
        'home_overall_over25_rate': 0.5,
        'home_overall_scored_diff': 0.0,
        'home_overall_xg_diff': 0.0,
        'home_overall_under_streak': 0,
        'home_overall_over_streak': 0,
        'home_overall_btts_streak': 0,
        
        'away_overall_goals_scored_avg': 1.1,
        'away_overall_goals_conceded_avg': 1.3,
        'away_overall_xg_avg': 1.1,
        'away_overall_btts_rate': 0.5,
        'away_overall_over25_rate': 0.5,
        'away_overall_scored_diff': 0.0,
        'away_overall_xg_diff': 0.0,
        'away_overall_under_streak': 0,
        'away_overall_over_streak': 0,
        'away_overall_btts_streak': 0,
        
        # Temporal Defaults
        'is_weekend': 1,
        'day_of_week': 5,
        'month': 1,
        'season_month_idx': 5
    }
    
    df = df.fillna(default_values)
    
    # Rename columns for consistency
    df = df.rename(columns={
        'Id': 'fixture_id',
        'ApiId': 'api_id',
        'Date': 'date',
        'LeagueId': 'league_id',
        'IsDerby': 'is_derby',
        'HomeWinOdds': 'home_win_odds',
        'DrawOdds': 'draw_odds',
        'AwayWinOdds': 'away_win_odds',
        'Over25Odds': 'over25_odds',
        'BttsYesOdds': 'btts_yes_odds',
        'HomeGoal': 'home_goals',
        'AwayGoal': 'away_goals',
        'TotalGoals': 'total_goals'
    })

    # Create target variables
    df['target_over25'] = (df['total_goals'] > 2.5).astype(int)
    df['target_btts'] = df['BTTS']
    df['target_goals_2_3'] = df['total_goals'].isin([2, 3]).astype(int)
    df['target_result'] = df.apply(
        lambda x: 0 if x['home_goals'] > x['away_goals'] else (1 if x['home_goals'] == x['away_goals'] else 2),
        axis=1
    )
    
    # Select columns for output
    output_cols = [
        'fixture_id', 'api_id', 'date', 'league_id',
        # Existing Home
        'home_goals_scored_avg', 'home_goals_conceded_avg', 'home_xg_avg',
        'home_shots_avg', 'home_shots_on_target_avg', 'home_btts_rate',
        'home_over25_rate', 'home_clean_sheet_rate', 'home_failed_to_score_rate',
        # Overall Home + Reversion + Streaks
        'home_overall_goals_scored_avg', 'home_overall_goals_conceded_avg',
        'home_overall_xg_avg', 'home_overall_btts_rate', 'home_overall_over25_rate',
        'home_overall_scored_diff', 'home_overall_xg_diff',
        'home_overall_under_streak', 'home_overall_over_streak', 'home_overall_btts_streak',
        
        # Existing Away
        'away_goals_scored_avg', 'away_goals_conceded_avg', 'away_xg_avg',
        'away_shots_avg', 'away_shots_on_target_avg', 'away_btts_rate',
        'away_over25_rate', 'away_clean_sheet_rate', 'away_failed_to_score_rate',
        # Overall Away + Reversion + Streaks
        'away_overall_goals_scored_avg', 'away_overall_goals_conceded_avg',
        'away_overall_xg_avg', 'away_overall_btts_rate', 'away_overall_over25_rate',
        'away_overall_scored_diff', 'away_overall_xg_diff',
        'away_overall_under_streak', 'away_overall_over_streak', 'away_overall_btts_streak',
        
        'h2h_total_goals_avg', 'h2h_btts_rate', 'h2h_over25_rate',
        'league_avg_goals', 'league_btts_rate', 'league_over25_rate',
        'is_derby',
        'is_weekend', 'day_of_week', 'month', 'season_month_idx',
        'home_win_odds', 'draw_odds', 'away_win_odds', 'over25_odds', 'btts_yes_odds',
        'home_goals', 'away_goals', 'total_goals',
        'target_over25', 'target_btts', 'target_goals_2_3', 'target_result'
    ]
    
    return df[output_cols]


def main():
    print("=" * 50)
    print("Football ML Feature Extraction (Optimized)")
    print("=" * 50)
    
    # Load data
    df = load_fixtures(DB_PATH)
    
    # Calculate rolling features
    df = calculate_rolling_features(df)
    
    # Calculate H2H features
    df = calculate_h2h_features(df)
    
    # Calculate Overall form features (Home+Away)
    df = calculate_overall_features(df)
    
    # Calculate Temporal features
    df = calculate_temporal_features(df)
    
    # Prepare final dataset
    result = prepare_training_data(df)
    
    # Drop first 100 rows (not enough history)
    result = result.iloc[100:].reset_index(drop=True)
    
    # Save to parquet
    result.to_parquet(OUTPUT_PATH, index=False)
    print(f"\nSaved {len(result)} samples to {OUTPUT_PATH}")
    
    # Print summary
    print("\n--- Target Distribution ---")
    print(f"Over 2.5: {result['target_over25'].mean():.1%}")
    print(f"BTTS: {result['target_btts'].mean():.1%}")
    print(f"2-3 Goals: {result['target_goals_2_3'].mean():.1%}")
    print(f"H/D/A: Home={result['target_result'].value_counts().get(0, 0)/len(result):.1%}, "
          f"Draw={result['target_result'].value_counts().get(1, 0)/len(result):.1%}, "
          f"Away={result['target_result'].value_counts().get(2, 0)/len(result):.1%}")


if __name__ == "__main__":
    main()
