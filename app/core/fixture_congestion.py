"""
Fixture Congestion Feature Extractor
Adds European competition context features to ML model using match date patterns
"""
import pandas as pd
from datetime import timedelta
from typing import Dict, List


# Teams that typically play in European competitions
EUROPEAN_TEAMS = {
    'E0': ['Liverpool', 'Man City', 'Arsenal', 'Chelsea', 'Tottenham', 'Man United', 'Newcastle', 'Aston Villa'],
    'SP1': ['Real Madrid', 'Barcelona', 'Atletico Madrid', 'Sevilla', 'Real Sociedad'],
    'D1': ['Bayern Munich', 'Dortmund', 'RB Leipzig', 'Leverkusen', 'Union Berlin'],
    'I1': ['Inter', 'AC Milan', 'Napoli', 'Juventus', 'Lazio', 'Roma', 'Atalanta'],
    'F1': ['PSG', 'Monaco', 'Marseille', 'Lens', 'Lille']
}


def calculate_fixture_congestion(team_name: str, league_id: str, match_date: pd.Timestamp, all_matches_df: pd.DataFrame) -> Dict:
    """
    Calculate fixture congestion features from match schedule.
    
    Returns dict with:
    - days_since_last_match
    - matches_last_7_days
    - matches_last_14_days
    - in_european_competition (team typically plays CL/EL)
    - likely_rotation_risk (has match within 3 days)
    """
    
    # Filter to team's matches
    team_matches = all_matches_df[
        ((all_matches_df['HomeTeam'] == team_name) | 
         (all_matches_df['AwayTeam'] == team_name)) &
        (all_matches_df['League'] == league_id) &
        (all_matches_df['Date'] < match_date)
    ].sort_values('Date')
    
    if len(team_matches) == 0:
        return {
            'days_since_last_match': 7,
            'matches_last_7_days': 1,
            'matches_last_14_days': 2,
            'in_european_competition': 0,
            'likely_rotation_risk': 0,
            'congestion_index': 0
        }
    
    # Days since last match
    last_match_date = team_matches.iloc[-1]['Date']
    days_since = (match_date - last_match_date).days
    
    # Matches in last 7 and 14 days
    cutoff_7 = match_date - timedelta(days=7)
    cutoff_14 = match_date - timedelta(days=14)
    
    matches_last_7 = len(team_matches[team_matches['Date'] >= cutoff_7])
    matches_last_14 = len(team_matches[team_matches['Date'] >= cutoff_14])
    
    # Check if team is in European competition
    in_european = 1 if team_name in EUROPEAN_TEAMS.get(league_id, []) else 0
    
    # Rotation risk: likely if match within 3-4 days (typical midweek European fixture)
    rotation_risk = 1 if (days_since <= 3 and in_european) else 0
    
    # Congestion index: 0-5 scale
    # 0 = Well-rested (7+ days)
    # 1 = Normal (5-7 days)
    # 2 = Tight (3-4 days, 1 match in last 7)
    # 3 = Congested (3 days, 2 matches in last 7)
    # 4 = Heavy congestion (2-3 days, 2+ matches in last 7)
    # 5 = Extreme (< 3 days, 3+ matches in last 7)
    
    if days_since >= 7:
        congestion = 0
    elif days_since >= 5:
        congestion = 1
    elif days_since >= 3:
        congestion = 2 if matches_last_7 <= 1 else 3
    else:  # < 3 days
        congestion = 4 if matches_last_7 <= 2 else 5
    
    return {
        'days_since_last_match': days_since,
        'matches_last_7_days': matches_last_7,
        'matches_last_14_days': matches_last_14,
        'in_european_competition': in_european,
        'likely_rotation_risk': rotation_risk,
        'congestion_index': congestion
    }


def add_congestion_features_to_match(home_team: str, away_team: str, league_id: str, 
                                      match_date: pd.Timestamp, all_matches_df: pd.DataFrame) -> Dict:
    """
    Add all fixture congestion features for both teams.
    """
    home_congestion = calculate_fixture_congestion(home_team, league_id, match_date, all_matches_df)
    away_congestion = calculate_fixture_congestion(away_team, league_id, match_date, all_matches_df)
    
    features = {}
    
    # Add home features with prefix
    for key, value in home_congestion.items():
        features[f'home_{key}'] = value
    
    # Add away features with prefix
    for key, value in away_congestion.items():
        features[f'away_{key}'] = value
    
    # Add comparative features
    features['congestion_diff'] = home_congestion['congestion_index'] - away_congestion['congestion_index']
    features['rest_advantage'] = home_congestion['days_since_last_match'] - away_congestion['days_since_last_match']
    features['both_in_europe'] = home_congestion['in_european_competition'] * away_congestion['in_european_competition']
    features['either_rotation_risk'] = max(home_congestion['likely_rotation_risk'], away_congestion['likely_rotation_risk'])
    
    return features


# Example usage in backtest:
"""
from fixture_congestion import add_congestion_features_to_match

# In your backtest loop:
for match in matches:
    home_team = match['HomeTeam']
    away_team = match['AwayTeam']
    match_date = match['Date']
    
    # Get congestion features
    congestion_features = add_congestion_features_to_match(
        home_team, away_team, league_id, match_date, all_historical_df
    )
    
    # Add to ML features
    ml_features.update(congestion_features)
"""


# Test function
if __name__ == "__main__":
    import sys
    from pathlib import Path
    
    PROJECT_ROOT = Path(__file__).parent.parent
    sys.path.insert(0, str(PROJECT_ROOT))
    
    DATA_DIR = PROJECT_ROOT / "data"
    
    # Load current season
    excel_file = list((DATA_DIR / "historical").glob("*2025-2026.xlsx"))[0]
    df = pd.read_excel(excel_file, sheet_name='E0')
    df['Date'] = pd.to_datetime(df['Date'])
    df['League'] = 'E0'
    
    # Test on a Liverpool match
    liverpool_match = df[df['HomeTeam'] == 'Liverpool'].iloc[5]
    
    print("=" * 70)
    print("FIXTURE CONGESTION TEST")
    print("=" * 70)
    print(f"\nMatch: {liverpool_match['HomeTeam']} vs {liverpool_match['AwayTeam']}")
    print(f"Date: {liverpool_match['Date']}")
    
    features = add_congestion_features_to_match(
        liverpool_match['HomeTeam'],
        liverpool_match['AwayTeam'],
        'E0',
        liverpool_match['Date'],
        df
    )
    
    print("\nFixture Congestion Features:")
    for key, value in features.items():
        print(f"  {key}: {value}")
    
    # Interpretation
    print("\n" + "=" * 70)
    print("INTERPRETATION")
    print("=" * 70)
    
    if features['home_congestion_index'] >= 3:
        print("⚠️  HOME TEAM: Fixture congestion detected!")
    if features['away_congestion_index'] >= 3:
        print("⚠️  AWAY TEAM: Fixture congestion detected!")
    if features['either_rotation_risk']:
        print("🔄 ROTATION RISK: One or both teams may rotate squad")
    if features['both_in_europe']:
        print("🏆 EUROPEAN CLASH: Both teams playing in European competitions")
