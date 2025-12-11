"""
ML Model Training Script with Fixture Congestion Features
Trains HDW, BTTS, and Over/Under models using historical Excel data
"""
import pandas as pd
import numpy as np
from pathlib import Path
from datetime import datetime, timedelta
import pickle
import json
from sklearn.model_selection import train_test_split
from sklearn.preprocessing import StandardScaler, LabelEncoder
from sklearn.metrics import accuracy_score, classification_report
from xgboost import XGBClassifier
import sys

# Add project root
PROJECT_ROOT = Path(__file__).parent.parent
sys.path.insert(0, str(PROJECT_ROOT))

from app.core.fixture_congestion import calculate_fixture_congestion, EUROPEAN_TEAMS

DATA_DIR = PROJECT_ROOT / "data" / "historical"
MODEL_DIR = PROJECT_ROOT / "models"
MODEL_DIR.mkdir(exist_ok=True)

# League mapping
LEAGUE_NAMES = {
    'E0': 'Premier League', 'E1': 'Championship', 'E2': 'League One', 'E3': 'League Two',
    'D1': 'Bundesliga', 'D2': '2. Bundesliga',
    'I1': 'Serie A', 'I2': 'Serie B',
    'F1': 'Ligue 1', 'F2': 'Ligue 2',
    'SP1': 'La Liga'
}


def load_all_historical_data():
    """Load all historical match data from Excel files."""
    print("=" * 70)
    print("LOADING HISTORICAL DATA")
    print("=" * 70)
    
    all_matches = []
    excel_files = sorted(DATA_DIR.glob("*.xlsx"))
    
    for excel_file in excel_files:
        print(f"\n📂 Loading: {excel_file.name}")
        try:
            xl = pd.ExcelFile(excel_file)
            for sheet_name in xl.sheet_names:
                if sheet_name not in LEAGUE_NAMES:
                    continue
                
                df = pd.read_excel(excel_file, sheet_name=sheet_name)
                
                # Must have results
                if 'FTR' not in df.columns or 'FTHG' not in df.columns:
                    continue
                
                df['Date'] = pd.to_datetime(df['Date'], errors='coerce')
                df['League'] = sheet_name
                df = df[df['FTR'].notna() & df['Date'].notna()]
                
                print(f"   ✅ {LEAGUE_NAMES[sheet_name]}: {len(df)} matches")
                all_matches.append(df)
                
        except Exception as e:
            print(f"   ⚠️ Error loading {excel_file.name}: {e}")
            continue
    
    if not all_matches:
        raise ValueError("No historical data found!")
    
    combined = pd.concat(all_matches, ignore_index=True)
    combined = combined.sort_values('Date').reset_index(drop=True)
    
    print(f"\n✅ Total: {len(combined):,} matches from {len(excel_files)} seasons")
    print(f"   Date range: {combined['Date'].min().strftime('%Y-%m-%d')} to {combined['Date'].max().strftime('%Y-%m-%d')}")
    
    return combined


def calculate_team_stats_from_history(team_name, league_id, before_date, all_matches_df):
    """Calculate team stats using only matches before the target date."""
    team_matches = all_matches_df[
        ((all_matches_df['HomeTeam'] == team_name) | (all_matches_df['AwayTeam'] == team_name)) &
        (all_matches_df['League'] == league_id) &
        (all_matches_df['Date'] < before_date)
    ].sort_values('Date')
    
    if len(team_matches) < 5:
        return None  # Not enough history
    
    recent = team_matches.tail(10)
    
    stats = {
        'played': len(recent),
        'wins': 0,
        'draws': 0,
        'losses': 0,
        'goals_for': 0,
        'goals_against': 0,
        'clean_sheets': 0,
        'form_points': 0
    }
    
    for _, match in recent.iterrows():
        is_home = match['HomeTeam'] == team_name
        
        if is_home:
            gf = match['FTHG'] or 0
            ga = match['FTAG'] or 0
            result = match['FTR']
        else:
            gf = match['FTAG'] or 0
            ga = match['FTHG'] or 0
            ftr = match['FTR']
            result = 'A' if ftr == 'H' else ('H' if ftr == 'A' else 'D')
        
        stats['goals_for'] += gf
        stats['goals_against'] += ga
        
        if ga == 0:
            stats['clean_sheets'] += 1
        
        if (result == 'H' and is_home) or (result == 'A' and not is_home):
            stats['wins'] += 1
            stats['form_points'] += 3
        elif result == 'D':
            stats['draws'] += 1
            stats['form_points'] += 1
        else:
            stats['losses'] += 1
    
    stats['avg_goals_for'] = stats['goals_for'] / stats['played']
    stats['avg_goals_against'] = stats['goals_against'] / stats['played']
    stats['win_rate'] = stats['wins'] / stats['played']
    stats['draw_rate'] = stats['draws'] / stats['played']
    stats['clean_sheet_rate'] = stats['clean_sheets'] / stats['played']
    
    return stats


def extract_features_from_match(row, all_matches_df, idx):
    """Extract all features including fixture congestion for a match."""
    
    home_team = row['HomeTeam']
    away_team = row['AwayTeam']
    league_id = row['League']
    match_date = row['Date']
    
    # Team stats (only using data before this match)
    home_stats = calculate_team_stats_from_history(home_team, league_id, match_date, all_matches_df)
    away_stats = calculate_team_stats_from_history(away_team, league_id, match_date, all_matches_df)
    
    if not home_stats or not away_stats:
        return None
    
    # Fixture congestion features
    home_congestion = calculate_fixture_congestion(home_team, league_id, match_date, all_matches_df)
    away_congestion = calculate_fixture_congestion(away_team, league_id, match_date, all_matches_df)
    
    # Build feature dict
    features = {}
    
    # Team stats (34 features: 17 per team)
    for prefix, stats in [('home', home_stats), ('away', away_stats)]:
        features[f'{prefix}_played'] = stats['played']
        features[f'{prefix}_wins'] = stats['wins']
        features[f'{prefix}_draws'] = stats['draws']
        features[f'{prefix}_losses'] = stats['losses']
        features[f'{prefix}_win_rate'] = stats['win_rate']
        features[f'{prefix}_draw_rate'] = stats['draw_rate']
        features[f'{prefix}_goals_for'] = stats['goals_for']
        features[f'{prefix}_goals_against'] = stats['goals_against']
        features[f'{prefix}_goal_diff'] = stats['goals_for'] - stats['goals_against']
        features[f'{prefix}_avg_goals_for'] = stats['avg_goals_for']
        features[f'{prefix}_avg_goals_against'] = stats['avg_goals_against']
        features[f'{prefix}_clean_sheets'] = stats['clean_sheets']
        features[f'{prefix}_clean_sheet_rate'] = stats['clean_sheet_rate']
        features[f'{prefix}_form_points'] = stats['form_points']
        features[f'{prefix}_loss_rate'] = stats['losses'] / stats['played']
        features[f'{prefix}_home_wins'] = 0  # Placeholder
        features[f'{prefix}_away_wins'] = 0  # Placeholder
    
    # Odds features (8 features)
    features['odds_home'] = row.get('B365H', row.get('PSH', 2.0)) or 2.0
    features['odds_draw'] = row.get('B365D', row.get('PSD', 3.3)) or 3.3
    features['odds_away'] = row.get('B365A', row.get('PSA', 3.5)) or 3.5
    features['implied_home'] = 1 / features['odds_home']
    features['implied_draw'] = 1 / features['odds_draw']
    features['implied_away'] = 1 / features['odds_away']
    features['odds_spread'] = features['odds_away'] - features['odds_home']
    features['bookmaker_margin'] = features['implied_home'] + features['implied_draw'] + features['implied_away'] - 1
    
    # Comparative features (4 features)
    features['win_rate_diff'] = features['home_win_rate'] - features['away_win_rate']
    features['goal_diff_diff'] = features['home_goal_diff'] - features['away_goal_diff']
    features['avg_goals_diff'] = features['home_avg_goals_for'] - features['away_avg_goals_for']
    features['form_diff'] = features['home_form_points'] - features['away_form_points']
    
    # Over 2.5 features (3 features)
    features['over25_odds'] = row.get('B365>2.5', 1.9) or 1.9
    features['under25_odds'] = row.get('B365<2.5', 2.0) or 2.0
    features['expected_total_goals'] = features['home_avg_goals_for'] + features['away_avg_goals_for']
    
    # NEW: Fixture congestion features (14 features)
    for prefix, congestion in [('home', home_congestion), ('away', away_congestion)]:
        features[f'{prefix}_days_since_last_match'] = congestion['days_since_last_match']
        features[f'{prefix}_matches_last_7_days'] = congestion['matches_last_7_days']
        features[f'{prefix}_matches_last_14_days'] = congestion['matches_last_14_days']
        features[f'{prefix}_in_european_competition'] = congestion['in_european_competition']
        features[f'{prefix}_likely_rotation_risk'] = congestion['likely_rotation_risk']
        features[f'{prefix}_congestion_index'] = congestion['congestion_index']
    
    features['congestion_diff'] = home_congestion['congestion_index'] - away_congestion['congestion_index']
    features['rest_advantage'] = home_congestion['days_since_last_match'] - away_congestion['days_since_last_match']
    
    # H2H placeholder features (10 features)
    for feat in ['h2h_matches', 'h2h_home_wins', 'h2h_draws', 'h2h_away_wins', 
                 'h2h_home_win_rate', 'h2h_draw_rate', 'h2h_away_win_rate',
                 'h2h_avg_goals', 'h2h_btts_rate', 'h2h_over25_rate']:
        features[feat] = 0
    
    # Target variables
    features['target_hdw'] = row['FTR']
    features['target_over25'] = 1 if (row['FTHG'] + row['FTAG']) > 2.5 else 0
    features['target_btts'] = 1 if (row['FTHG'] > 0 and row['FTAG'] > 0) else 0
    
    return features


def prepare_training_data(all_matches_df):
    """Extract features from all matches."""
    print("\n" + "=" * 70)
    print("EXTRACTING FEATURES")
    print("=" * 70)
    
    all_features = []
    
    for idx, row in all_matches_df.iterrows():
        if idx % 1000 == 0:
            print(f"   Processing match {idx:,}/{len(all_matches_df):,}...")
        
        try:
            features = extract_features_from_match(row, all_matches_df, idx)
            if features:
                all_features.append(features)
        except Exception as e:
            if len(all_features) < 10:
                print(f"   ⚠️ Error on row {idx}: {e}")
            continue
    
    print(f"\n✅ Extracted features from {len(all_features):,} matches")
    
    df_features = pd.DataFrame(all_features)
    return df_features


def train_models(df_features):
    """Train HDW, BTTS, and Over/Under models."""
    print("\n" + "=" * 70)
    print("TRAINING MODELS")
    print("=" * 70)
    
    # Separate features and targets
    target_cols = ['target_hdw', 'target_over25', 'target_btts']
    feature_cols = [col for col in df_features.columns if col not in target_cols]
    
    X = df_features[feature_cols].values
    feature_names = feature_cols
    
    print(f"\n📊 Training Data:")
    print(f"   Features: {len(feature_cols)} (including {sum('congestion' in f or 'days_since' in f or 'matches_last' in f for f in feature_cols)} congestion features)")
    print(f"   Samples: {len(X):,}")
    
    # Scale features
    scaler = StandardScaler()
    X_scaled = scaler.fit_transform(X)
    
    models = {}
    encoders = {}
    
    # === HDW Model ===
    print(f"\n🏆 Training HDW (Home/Draw/Away) Model...")
    y_hdw = df_features['target_hdw'].values
    
    encoder_hdw = LabelEncoder()
    y_hdw_encoded = encoder_hdw.fit_transform(y_hdw)
    
    X_train, X_test, y_train, y_test = train_test_split(X_scaled, y_hdw_encoded, test_size=0.2, random_state=42)
    
    hdw_model = XGBClassifier(
        max_depth=6,
        learning_rate=0.1,
        n_estimators=200,
        random_state=42,
        eval_metric='mlogloss'
    )
    hdw_model.fit(X_train, y_train)
    
    y_pred = hdw_model.predict(X_test)
    accuracy = accuracy_score(y_test, y_pred)
    print(f"   ✅ HDW Model Accuracy: {accuracy:.1%}")
    
    models['hdw'] = hdw_model
    encoders['hdw'] = encoder_hdw
    
    # === Over 2.5 Model ===
    print(f"\n⚽ Training Over 2.5 Model...")
    y_over25 = df_features['target_over25'].values
    
    X_train, X_test, y_train, y_test = train_test_split(X_scaled, y_over25, test_size=0.2, random_state=42)
    
    over25_model = XGBClassifier(
        max_depth=5,
        learning_rate=0.1,
        n_estimators=150,
        random_state=42
    )
    over25_model.fit(X_train, y_train)
    
    y_pred = over25_model.predict(X_test)
    accuracy = accuracy_score(y_test, y_pred)
    print(f"   ✅ Over 2.5 Model Accuracy: {accuracy:.1%}")
    
    models['over25'] = over25_model
    
    # === BTTS Model ===
    print(f"\n🎯 Training BTTS (Both Teams To Score) Model...")
    y_btts = df_features['target_btts'].values
    
    X_train, X_test, y_train, y_test = train_test_split(X_scaled, y_btts, test_size=0.2, random_state=42)
    
    btts_model = XGBClassifier(
        max_depth=5,
        learning_rate=0.1,
        n_estimators=150,
        random_state=42
    )
    btts_model.fit(X_train, y_train)
    
    y_pred = btts_model.predict(X_test)
    accuracy = accuracy_score(y_test, y_pred)
    print(f"   ✅ BTTS Model Accuracy: {accuracy:.1%}")
    
    models['btts'] = btts_model
    
    return models, encoders, scaler, feature_names


def save_models(models, encoders, scaler, feature_names):
    """Save all models and metadata."""
    print("\n" + "=" * 70)
    print("SAVING MODELS")
    print("=" * 70)
    
    # Save models
    with open(MODEL_DIR / "hdw_model.pkl", 'wb') as f:
        pickle.dump(models['hdw'], f)
    print(f"   ✅ Saved hdw_model.pkl")
    
    with open(MODEL_DIR / "over25_model.pkl", 'wb') as f:
        pickle.dump(models['over25'], f)
    print(f"   ✅ Saved over25_model.pkl")
    
    with open(MODEL_DIR / "btts_model.pkl", 'wb') as f:
        pickle.dump(models['btts'], f)
    print(f"   ✅ Saved btts_model.pkl")
    
    # Save scaler
    with open(MODEL_DIR / "scaler.pkl", 'wb') as f:
        pickle.dump(scaler, f)
    print(f"   ✅ Saved scaler.pkl")
    
    # Save encoder
    with open(MODEL_DIR / "hdw_encoder.pkl", 'wb') as f:
        pickle.dump(encoders['hdw'], f)
    print(f"   ✅ Saved hdw_encoder.pkl")
    
    # Save metadata
    metadata = {
        "feature_names": feature_names,
        "n_features": len(feature_names),
        "trained_date": datetime.now().isoformat(),
        "includes_congestion_features": True
    }
    
    with open(MODEL_DIR / "model_metadata.json", 'w') as f:
        json.dump(metadata, f, indent=2)
    print(f"   ✅ Saved model_metadata.json")
    
    print(f"\n✅ All models saved to: {MODEL_DIR}")


def main():
    print("\n🚀 ML MODEL TRAINING WITH FIXTURE CONGESTION")
    print(f"   Starting at: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    
    # Load data
    all_matches = load_all_historical_data()
    
    # Extract features
    df_features = prepare_training_data(all_matches)
    
    # Train models
    models, encoders, scaler, feature_names = train_models(df_features)
    
    # Save models
    save_models(models, encoders, scaler, feature_names)
    
    print("\n" + "=" * 70)
    print("✅ TRAINING COMPLETE!")
    print("=" * 70)
    print(f"\nNew models include {sum('congestion' in f or 'days_since' in f or 'matches_last' in f for f in feature_names)} fixture congestion features!")
    print(f"\nModels ready to use in: {MODEL_DIR}")
    

if __name__ == "__main__":
    main()
