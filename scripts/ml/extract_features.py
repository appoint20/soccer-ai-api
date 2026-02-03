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
    }
    
    df = df.fillna(default_values)
    
    # Create target variables
    df['target_over25'] = (df['TotalGoals'] > 2.5).astype(int)
    df['target_btts'] = df['BTTS']
    df['target_goals_2_3'] = df['TotalGoals'].isin([2, 3]).astype(int)
    df['target_result'] = df.apply(
        lambda x: 0 if x['HomeGoal'] > x['AwayGoal'] else (1 if x['HomeGoal'] == x['AwayGoal'] else 2),
        axis=1
    )
    
    # Select columns for output
    feature_cols = [
        'Id', 'ApiId', 'Date', 'LeagueId',
        'home_goals_scored_avg', 'home_goals_conceded_avg', 'home_xg_avg',
        'home_shots_avg', 'home_shots_on_target_avg', 'home_btts_rate',
        'home_over25_rate', 'home_clean_sheet_rate', 'home_failed_to_score_rate',
        'away_goals_scored_avg', 'away_goals_conceded_avg', 'away_xg_avg',
        'away_shots_avg', 'away_shots_on_target_avg', 'away_btts_rate',
        'away_over25_rate', 'away_clean_sheet_rate', 'away_failed_to_score_rate',
        'h2h_total_goals_avg', 'h2h_btts_rate', 'h2h_over25_rate',
        'league_avg_goals', 'league_btts_rate', 'league_over25_rate',
        'IsDerby',
        'HomeWinOdds', 'DrawOdds', 'AwayWinOdds', 'Over25Odds', 'BttsYesOdds',
        'HomeGoal', 'AwayGoal', 'TotalGoals',
        'target_over25', 'target_btts', 'target_goals_2_3', 'target_result'
    ]
    
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
    
    output_cols = [
        'fixture_id', 'api_id', 'date', 'league_id',
        'home_goals_scored_avg', 'home_goals_conceded_avg', 'home_xg_avg',
        'home_shots_avg', 'home_shots_on_target_avg', 'home_btts_rate',
        'home_over25_rate', 'home_clean_sheet_rate', 'home_failed_to_score_rate',
        'away_goals_scored_avg', 'away_goals_conceded_avg', 'away_xg_avg',
        'away_shots_avg', 'away_shots_on_target_avg', 'away_btts_rate',
        'away_over25_rate', 'away_clean_sheet_rate', 'away_failed_to_score_rate',
        'h2h_total_goals_avg', 'h2h_btts_rate', 'h2h_over25_rate',
        'league_avg_goals', 'league_btts_rate', 'league_over25_rate',
        'is_derby',
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
