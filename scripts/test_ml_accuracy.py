"""Test ML accuracy on 150+ matches - NO GEMINI, PURE ML"""
import pandas as pd
import json
from pathlib import Path

DATA_DIR = Path('data')
LEAGUE_FOLDERS = {
    'E0': 'Premier_League', 'E1': 'Championship', 'E2': 'League_One', 'E3': 'League_Two',
    'D1': 'Bundesliga', 'D2': '2_Bundesliga',
    'I1': 'Serie_A', 'I2': 'Serie_B',
    'F1': 'Ligue_1', 'F2': 'Ligue_2',
    'SP1': 'La_Liga'
}

# Load raw stats cache
cache = {}
for league_id, folder_name in LEAGUE_FOLDERS.items():
    teams_dir = DATA_DIR / 'team_stats' / folder_name / '2025' / 'teams'
    if not teams_dir.exists():
        continue
    for f in teams_dir.glob('*_stats.json'):
        with open(f) as fp:
            data = json.load(fp)
            name = data.get('team', {}).get('name', '')
            if name:
                cache[name.lower()] = data
                cache[f'{league_id}:{name.lower()}'] = data

print(f'Stats cache: {len(cache)} entries')

# Load ALL matches from multiple leagues
all_matches = []
for sheet in ['E0', 'E1', 'D1', 'I1', 'SP1']:
    try:
        df = pd.read_excel(DATA_DIR / 'historical/all-euro-data-2025-2026.xlsx', sheet_name=sheet)
        df['Date'] = pd.to_datetime(df['Date'], errors='coerce')
        df['League'] = sheet
        df = df[df['FTR'].notna()]
        df = df[df['Date'] >= '2025-10-01']
        all_matches.append(df)
    except Exception as e:
        print(f'Error loading {sheet}: {e}')

df = pd.concat(all_matches, ignore_index=True)
print(f'Total matches loaded: {len(df)}')

# Initialize ML predictor (NO GEMINI!)
from app.core.ml_predictor import get_ml_predictor
from app.core.poisson import PoissonPredictor

ml = get_ml_predictor()
poisson = PoissonPredictor()

# Test on first 150 matches
correct_ml = 0
correct_poisson = 0
total = 0
skipped = 0

for _, row in df.head(200).iterrows():
    if total >= 150:
        break
        
    home_name = row['HomeTeam']
    away_name = row['AwayTeam']
    league_id = row['League']
    
    home_stats = cache.get(f'{league_id}:{home_name.lower()}', {})
    away_stats = cache.get(f'{league_id}:{away_name.lower()}', {})
    
    if not home_stats or not away_stats:
        skipped += 1
        continue
    
    odds = {
        'home': float(row.get('B365H', 2.0) or 2.0),
        'draw': float(row.get('B365D', 3.3) or 3.3),
        'away': float(row.get('B365A', 3.5) or 3.5)
    }
    
    # ML prediction - PURE XGBoost, NO GEMINI
    ml_result = ml.predict(home_stats, away_stats, odds, {})
    ml_pred = ml_result.get('prediction', 'H')
    
    # Poisson prediction
    goals = home_stats.get('goals', {})
    home_attack = float(goals.get('for', {}).get('average', {}).get('total', '1.3') or '1.3')
    home_defense = float(goals.get('against', {}).get('average', {}).get('total', '1.1') or '1.1')
    
    goals = away_stats.get('goals', {})
    away_attack = float(goals.get('for', {}).get('average', {}).get('total', '1.1') or '1.1')
    away_defense = float(goals.get('against', {}).get('average', {}).get('total', '1.3') or '1.3')
    
    poisson_result = poisson.predict(home_attack, away_attack, home_defense, away_defense)
    poisson_pred = poisson_result.get('hdw', 'H')
    
    actual = row['FTR']
    total += 1
    
    if ml_pred == actual:
        correct_ml += 1
    if poisson_pred == actual:
        correct_poisson += 1

print(f'\n=== PURE ML (XGBoost) vs Poisson - NO GEMINI ===')
print(f'Tested: {total} matches (skipped {skipped} without stats)')
print(f'')
print(f'ML Accuracy:      {correct_ml}/{total} = {correct_ml/total*100:.1f}%')
print(f'Poisson Accuracy: {correct_poisson}/{total} = {correct_poisson/total*100:.1f}%')
