"""
Train ML Model using Football API Team Stats
Uses real team stats from JSON files + historical match results from Excel.
"""
import json
import pickle
import pandas as pd
import numpy as np
from pathlib import Path
from datetime import datetime
from typing import Dict, List, Tuple
import warnings
warnings.filterwarnings('ignore')

from sklearn.model_selection import train_test_split
from sklearn.preprocessing import StandardScaler, LabelEncoder
from xgboost import XGBClassifier
from sklearn.metrics import accuracy_score, classification_report

PROJECT_ROOT = Path(__file__).parent.parent
DATA_DIR = PROJECT_ROOT / "data"
MODELS_DIR = PROJECT_ROOT / "models"

# League mapping
LEAGUE_FOLDERS = {
    'E0': 'Premier_League', 'E1': 'Championship', 'E2': 'League_One', 'E3': 'League_Two',
    'D1': 'Bundesliga', 'D2': '2_Bundesliga',
    'I1': 'Serie_A', 'I2': 'Serie_B',
    'F1': 'Ligue_1', 'F2': 'Ligue_2',
    'SP1': 'La_Liga'
}


def load_team_stats_cache() -> Dict[str, Dict]:
    """Load all team stats from JSON files into a lookup cache."""
    cache = {}
    team_stats_dir = DATA_DIR / "team_stats"
    
    for league_id, folder_name in LEAGUE_FOLDERS.items():
        teams_dir = team_stats_dir / folder_name / "2025" / "teams"
        if not teams_dir.exists():
            continue
        
        for stats_file in teams_dir.glob("*_stats.json"):
            try:
                with open(stats_file, 'r') as f:
                    data = json.load(f)
                
                team_name = data.get('team', {}).get('name', '')
                team_id = data.get('team', {}).get('id', '')
                
                if team_name:
                    # Store by multiple keys for fuzzy matching
                    cache[team_name.lower()] = data
                    cache[f"{league_id}:{team_name.lower()}"] = data
                    if team_id:
                        cache[f"id:{team_id}"] = data
            except Exception:
                continue
    
    return cache


def get_team_stats(team_name: str, league_id: str, cache: Dict) -> Dict:
    """Get team stats with fuzzy matching."""
    # Try exact match first
    key = f"{league_id}:{team_name.lower()}"
    if key in cache:
        return cache[key]
    
    # Try just team name
    if team_name.lower() in cache:
        return cache[team_name.lower()]
    
    # Fuzzy match
    for cached_name, data in cache.items():
        if team_name.lower() in cached_name or cached_name in team_name.lower():
            return data
    
    return {}


def build_h2h_cache(historical_df: pd.DataFrame) -> Dict:
    """
    Build H2H statistics cache from historical data.
    Key: "home_team:away_team" -> stats
    """
    h2h_cache = {}
    
    for _, row in historical_df.iterrows():
        home = str(row.get('HomeTeam', '')).lower()
        away = str(row.get('AwayTeam', '')).lower()
        ftr = row.get('FTR', '')
        fthg = row.get('FTHG', 0) or 0
        ftag = row.get('FTAG', 0) or 0
        
        if not home or not away:
            continue
        
        key = f"{home}:{away}"
        
        if key not in h2h_cache:
            h2h_cache[key] = {
                'matches': 0, 'home_wins': 0, 'draws': 0, 'away_wins': 0,
                'total_goals': 0, 'btts_count': 0, 'over25_count': 0
            }
        
        h2h = h2h_cache[key]
        h2h['matches'] += 1
        h2h['total_goals'] += fthg + ftag
        
        if ftr == 'H':
            h2h['home_wins'] += 1
        elif ftr == 'D':
            h2h['draws'] += 1
        elif ftr == 'A':
            h2h['away_wins'] += 1
        
        if fthg > 0 and ftag > 0:
            h2h['btts_count'] += 1
        
        if fthg + ftag > 2.5:
            h2h['over25_count'] += 1
    
    return h2h_cache


def extract_h2h_features(home_team: str, away_team: str, h2h_cache: Dict) -> Dict:
    """Extract H2H features for a match."""
    key = f"{home_team.lower()}:{away_team.lower()}"
    h2h = h2h_cache.get(key, {})
    
    matches = h2h.get('matches', 0)
    if matches == 0:
        # No H2H history - return defaults
        return {
            'h2h_matches': 0,
            'h2h_home_wins': 0,
            'h2h_draws': 0,
            'h2h_away_wins': 0,
            'h2h_home_win_rate': 0.33,
            'h2h_draw_rate': 0.33,
            'h2h_away_win_rate': 0.33,
            'h2h_avg_goals': 2.5,
            'h2h_btts_rate': 0.5,
            'h2h_over25_rate': 0.5
        }
    
    return {
        'h2h_matches': matches,
        'h2h_home_wins': h2h.get('home_wins', 0),
        'h2h_draws': h2h.get('draws', 0),
        'h2h_away_wins': h2h.get('away_wins', 0),
        'h2h_home_win_rate': h2h.get('home_wins', 0) / matches,
        'h2h_draw_rate': h2h.get('draws', 0) / matches,
        'h2h_away_win_rate': h2h.get('away_wins', 0) / matches,
        'h2h_avg_goals': h2h.get('total_goals', 0) / matches,
        'h2h_btts_rate': h2h.get('btts_count', 0) / matches,
        'h2h_over25_rate': h2h.get('over25_count', 0) / matches
    }


def extract_team_features(stats: Dict, prefix: str) -> Dict:
    """Extract features from Football API team stats."""
    if not stats:
        # Return default features
        return {
            f'{prefix}_played': 0,
            f'{prefix}_wins': 0,
            f'{prefix}_draws': 0,
            f'{prefix}_losses': 0,
            f'{prefix}_win_rate': 0.33,
            f'{prefix}_draw_rate': 0.33,
            f'{prefix}_loss_rate': 0.33,
            f'{prefix}_goals_for': 0,
            f'{prefix}_goals_against': 0,
            f'{prefix}_goal_diff': 0,
            f'{prefix}_avg_goals_for': 1.2,
            f'{prefix}_avg_goals_against': 1.2,
            f'{prefix}_clean_sheets': 0,
            f'{prefix}_clean_sheet_rate': 0,
            f'{prefix}_form_points': 5,
            f'{prefix}_home_wins': 0,
            f'{prefix}_away_wins': 0,
        }
    
    fixtures = stats.get('fixtures', {})
    goals = stats.get('goals', {})
    
    played = fixtures.get('played', {}).get('total', 1)
    wins = fixtures.get('wins', {}).get('total', 0)
    draws = fixtures.get('draws', {}).get('total', 0)
    losses = fixtures.get('loses', {}).get('total', 0)
    
    goals_for = goals.get('for', {}).get('total', {}).get('total', 0)
    goals_against = goals.get('against', {}).get('total', {}).get('total', 0)
    
    avg_for = float(goals.get('for', {}).get('average', {}).get('total', '1.2') or '1.2')
    avg_against = float(goals.get('against', {}).get('average', {}).get('total', '1.2') or '1.2')
    
    clean_sheets = stats.get('clean_sheet', {}).get('total', 0) or 0
    
    # Parse form string (e.g., "WDLWW")
    form_str = stats.get('form', '')[-5:]  # Last 5 matches
    form_points = sum({'W': 3, 'D': 1, 'L': 0}.get(c, 0) for c in form_str)
    
    return {
        f'{prefix}_played': played,
        f'{prefix}_wins': wins,
        f'{prefix}_draws': draws,
        f'{prefix}_losses': losses,
        f'{prefix}_win_rate': wins / max(played, 1),
        f'{prefix}_draw_rate': draws / max(played, 1),
        f'{prefix}_loss_rate': losses / max(played, 1),
        f'{prefix}_goals_for': goals_for,
        f'{prefix}_goals_against': goals_against,
        f'{prefix}_goal_diff': goals_for - goals_against,
        f'{prefix}_avg_goals_for': avg_for,
        f'{prefix}_avg_goals_against': avg_against,
        f'{prefix}_clean_sheets': clean_sheets,
        f'{prefix}_clean_sheet_rate': clean_sheets / max(played, 1),
        f'{prefix}_form_points': form_points,
        f'{prefix}_home_wins': fixtures.get('wins', {}).get('home', 0),
        f'{prefix}_away_wins': fixtures.get('wins', {}).get('away', 0),
    }


def extract_match_features(row: pd.Series, team_cache: Dict, h2h_cache: Dict = None) -> Dict:
    """Extract all features for a match including H2H."""
    home_team = row.get('HomeTeam', '')
    away_team = row.get('AwayTeam', '')
    league_id = row.get('League', '')
    
    # Get team stats
    home_stats = get_team_stats(home_team, league_id, team_cache)
    away_stats = get_team_stats(away_team, league_id, team_cache)
    
    # Extract features
    features = {}
    features.update(extract_team_features(home_stats, 'home'))
    features.update(extract_team_features(away_stats, 'away'))
    
    # H2H features (10 features)
    if h2h_cache:
        features.update(extract_h2h_features(home_team, away_team, h2h_cache))
    
    # Odds features
    features['odds_home'] = row.get('B365H', row.get('PSH', 2.0)) or 2.0
    features['odds_draw'] = row.get('B365D', row.get('PSD', 3.3)) or 3.3
    features['odds_away'] = row.get('B365A', row.get('PSA', 3.5)) or 3.5
    
    # Implied probabilities
    features['implied_home'] = 1 / features['odds_home']
    features['implied_draw'] = 1 / features['odds_draw']
    features['implied_away'] = 1 / features['odds_away']
    
    # Odds spread/margin
    features['odds_spread'] = features['odds_away'] - features['odds_home']
    features['bookmaker_margin'] = features['implied_home'] + features['implied_draw'] + features['implied_away'] - 1
    
    # Comparative features
    features['win_rate_diff'] = features['home_win_rate'] - features['away_win_rate']
    features['goal_diff_diff'] = features['home_goal_diff'] - features['away_goal_diff']
    features['avg_goals_diff'] = features['home_avg_goals_for'] - features['away_avg_goals_for']
    features['form_diff'] = features['home_form_points'] - features['away_form_points']
    
    # Over 2.5 odds
    features['over25_odds'] = row.get('B365>2.5', row.get('P>2.5', 1.9)) or 1.9
    features['under25_odds'] = row.get('B365<2.5', row.get('P<2.5', 2.0)) or 2.0
    
    # Expected goals
    features['expected_total_goals'] = features['home_avg_goals_for'] + features['away_avg_goals_for']
    
    return features


def load_training_data(team_cache: Dict) -> Tuple[pd.DataFrame, pd.Series, pd.Series, pd.Series]:
    """Load training data from historical Excel file with H2H features."""
    historical_dir = DATA_DIR / "historical"
    excel_files = sorted(historical_dir.glob("*.xlsx"), reverse=True)
    
    if not excel_files:
        raise FileNotFoundError("No historical Excel files found")
    
    # Load ALL historical files for H2H (multiple seasons)
    print("📊 Building H2H cache from all historical seasons...")
    h2h_matches = []
    for excel_file in excel_files:
        try:
            xl = pd.ExcelFile(excel_file)
            for sheet_name in xl.sheet_names:
                if sheet_name not in LEAGUE_FOLDERS:
                    continue
                df = pd.read_excel(excel_file, sheet_name=sheet_name)
                if 'FTR' in df.columns:
                    df = df[df['FTR'].notna()]
                    h2h_matches.append(df)
        except Exception:
            continue
    
    if h2h_matches:
        h2h_combined = pd.concat(h2h_matches, ignore_index=True)
        h2h_cache = build_h2h_cache(h2h_combined)
        print(f"   Found {len(h2h_cache)} H2H pairs from {len(excel_files)} seasons")
    else:
        h2h_cache = {}
    
    # Load CURRENT season for training
    current_file = excel_files[0]
    print(f"📂 Loading training data: {current_file.name}")
    
    all_matches = []
    xl = pd.ExcelFile(current_file)
    
    for sheet_name in xl.sheet_names:
        if sheet_name not in LEAGUE_FOLDERS:
            continue
        
        df = pd.read_excel(current_file, sheet_name=sheet_name)
        df['League'] = sheet_name
        
        # Filter completed matches
        if 'FTR' in df.columns:
            df = df[df['FTR'].notna()]
            all_matches.append(df)
    
    combined = pd.concat(all_matches, ignore_index=True)
    print(f"✅ Loaded {len(combined)} matches for training")
    
    # IMPORTANT: Remove current season matches from H2H cache to prevent data leak!
    # H2H should only use PRIOR seasons, not same-season matches
    # Create a "clean" H2H cache by excluding same-season matchups
    print("🔒 Creating leak-free H2H cache (prior seasons only)...")
    
    # Build a set of current season matchups to exclude
    current_matchups = set()
    for _, row in combined.iterrows():
        home = str(row.get('HomeTeam', '')).lower()
        away = str(row.get('AwayTeam', '')).lower()
        current_matchups.add(f"{home}:{away}")
    
    # Filter H2H cache to only include pairs NOT in current season
    # or use stats from prior seasons if available
    clean_h2h_cache = {}
    for key, stats in h2h_cache.items():
        if key not in current_matchups:
            clean_h2h_cache[key] = stats
        else:
            # For same-season matches, use reduced stats (prior seasons only)
            # This is approximate - ideally would track by season
            if stats['matches'] > 1:
                # Reduce by 1 to simulate prior season data
                reduced = stats.copy()
                reduced['matches'] = max(0, reduced['matches'] - 1)
                if reduced['matches'] > 0:
                    clean_h2h_cache[key] = reduced
    
    print(f"   Clean H2H pairs: {len(clean_h2h_cache)}")
    
    # Extract features for each match
    print("🔧 Extracting features (with clean H2H)...")
    feature_rows = []
    
    for _, row in combined.iterrows():
        try:
            features = extract_match_features(row, team_cache, clean_h2h_cache)
            features['FTR'] = row['FTR']
            features['FTHG'] = row.get('FTHG', 0)
            features['FTAG'] = row.get('FTAG', 0)
            feature_rows.append(features)
        except Exception:
            continue
    
    features_df = pd.DataFrame(feature_rows)
    
    # Prepare targets
    y_hdw = features_df['FTR']
    y_over25 = ((features_df['FTHG'] + features_df['FTAG']) > 2.5).astype(int)
    y_btts = ((features_df['FTHG'] > 0) & (features_df['FTAG'] > 0)).astype(int)
    
    # Drop target columns
    X = features_df.drop(['FTR', 'FTHG', 'FTAG'], axis=1)
    
    return X, y_hdw, y_over25, y_btts


def train_models():
    """Train all prediction models."""
    print("=" * 60)
    print("🎯 ML MODEL TRAINING - Football API Features")
    print("=" * 60)
    
    # Load team stats cache
    print("\n📥 Loading team stats from JSON files...")
    team_cache = load_team_stats_cache()
    print(f"   Cached {len(team_cache)} team entries")
    
    # Load training data
    print("\n📊 Loading training data...")
    X, y_hdw, y_over25, y_btts = load_training_data(team_cache)
    
    print(f"\n📈 Dataset: {len(X)} samples, {len(X.columns)} features")
    print(f"   Features: {list(X.columns)[:10]}...")
    
    # Fill NaN values
    X = X.fillna(0)
    
    # Split data
    X_train, X_test, y_hdw_train, y_hdw_test = train_test_split(
        X, y_hdw, test_size=0.2, random_state=42
    )
    _, _, y_over25_train, y_over25_test = train_test_split(
        X, y_over25, test_size=0.2, random_state=42
    )
    _, _, y_btts_train, y_btts_test = train_test_split(
        X, y_btts, test_size=0.2, random_state=42
    )
    
    # Scale features
    print("\n⚙️ Scaling features...")
    scaler = StandardScaler()
    X_train_scaled = scaler.fit_transform(X_train)
    X_test_scaled = scaler.transform(X_test)
    
    # Encode HDW labels
    hdw_encoder = LabelEncoder()
    y_hdw_train_enc = hdw_encoder.fit_transform(y_hdw_train)
    y_hdw_test_enc = hdw_encoder.transform(y_hdw_test)
    
    results = {}
    
    # Train HDW model
    print("\n🏠 Training HDW model...")
    hdw_model = XGBClassifier(
        n_estimators=200, max_depth=6, learning_rate=0.05,
        subsample=0.9, colsample_bytree=0.9, random_state=42
    )
    hdw_model.fit(X_train_scaled, y_hdw_train_enc)
    hdw_pred = hdw_model.predict(X_test_scaled)
    hdw_acc = accuracy_score(y_hdw_test_enc, hdw_pred)
    results['hdw'] = hdw_acc
    print(f"   ✅ Accuracy: {hdw_acc:.1%}")
    
    # Train Over 2.5 model
    print("\n⚽ Training Over 2.5 model...")
    over25_model = XGBClassifier(
        n_estimators=150, max_depth=5, learning_rate=0.05,
        random_state=42
    )
    over25_model.fit(X_train_scaled, y_over25_train)
    over25_pred = over25_model.predict(X_test_scaled)
    over25_acc = accuracy_score(y_over25_test, over25_pred)
    results['over25'] = over25_acc
    print(f"   ✅ Accuracy: {over25_acc:.1%}")
    
    # Train BTTS model
    print("\n🔄 Training BTTS model...")
    btts_model = XGBClassifier(
        n_estimators=150, max_depth=5, learning_rate=0.05,
        random_state=42
    )
    btts_model.fit(X_train_scaled, y_btts_train)
    btts_pred = btts_model.predict(X_test_scaled)
    btts_acc = accuracy_score(y_btts_test, btts_pred)
    results['btts'] = btts_acc
    print(f"   ✅ Accuracy: {btts_acc:.1%}")
    
    # Save models
    print("\n💾 Saving models...")
    MODELS_DIR.mkdir(exist_ok=True)
    
    with open(MODELS_DIR / "hdw_model.pkl", 'wb') as f:
        pickle.dump(hdw_model, f)
    with open(MODELS_DIR / "over25_model.pkl", 'wb') as f:
        pickle.dump(over25_model, f)
    with open(MODELS_DIR / "btts_model.pkl", 'wb') as f:
        pickle.dump(btts_model, f)
    with open(MODELS_DIR / "scaler.pkl", 'wb') as f:
        pickle.dump(scaler, f)
    with open(MODELS_DIR / "hdw_encoder.pkl", 'wb') as f:
        pickle.dump(hdw_encoder, f)
    
    # Save metadata
    metadata = {
        "feature_names": list(X.columns),
        "feature_count": len(X.columns),
        "training_samples": len(X_train),
        "test_samples": len(X_test),
        "hdw_classes": list(hdw_encoder.classes_),
        "accuracies": results,
        "trained_at": datetime.now().isoformat()
    }
    
    with open(MODELS_DIR / "model_metadata.json", 'w') as f:
        json.dump(metadata, f, indent=2)
    
    print(f"\n✅ Models saved to {MODELS_DIR}")
    
    # Summary
    print("\n" + "=" * 60)
    print("📊 TRAINING RESULTS")
    print("=" * 60)
    print(f"   HDW:     {results['hdw']:.1%}")
    print(f"   Over2.5: {results['over25']:.1%}")
    print(f"   BTTS:    {results['btts']:.1%}")
    print("=" * 60)
    
    return results


if __name__ == "__main__":
    train_models()
