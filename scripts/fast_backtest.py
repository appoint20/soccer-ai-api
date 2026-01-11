#!/usr/bin/env python3
"""
Fast Backtest - 150+ matches, accuracy report by league.
Uses pickle cache to speed up Excel loading.
NO GEMINI - ML + Poisson + Monte Carlo only.
"""
import pandas as pd
import json
from pathlib import Path
from datetime import datetime, timedelta
import warnings
import sys
warnings.filterwarnings('ignore')

PROJECT_ROOT = Path(__file__).parent.parent
sys.path.insert(0, str(PROJECT_ROOT))

DATA_DIR = PROJECT_ROOT / "data"
CACHE_DIR = DATA_DIR / "cache"
CACHE_FILE = CACHE_DIR / "historical_matches.pkl"

LEAGUE_FOLDERS = {
    'E0': 'Premier_League', 'E1': 'Championship', 
    'D1': 'Bundesliga', 'I1': 'Serie_A', 'SP1': 'La_Liga'
}
LEAGUE_NAMES = {
    'E0': 'Premier League', 'E1': 'Championship', 
    'D1': 'Bundesliga', 'I1': 'Serie A', 'SP1': 'La Liga'
}

def load_team_stats_cache():
    """Load team stats from JSON files."""
    cache = {}
    for lid, folder in LEAGUE_FOLDERS.items():
        teams_dir = DATA_DIR / 'team_stats' / folder / '2025' / 'teams'
        if teams_dir.exists():
            for f in teams_dir.glob('*_stats.json'):
                try:
                    data = json.load(open(f))
                    name = data.get('team', {}).get('name', '')
                    if name:
                        cache[f'{lid}:{name.lower()}'] = data
                except: pass
    return cache

def load_matches():
    """Load matches from cache or Excel."""
    CACHE_DIR.mkdir(exist_ok=True)
    
    if CACHE_FILE.exists():
        print(f"📦 Loading from cache...")
        return pd.read_pickle(CACHE_FILE)
    
    print("📂 Loading from Excel (building cache)...")
    all_matches = []
    for sheet in LEAGUE_FOLDERS.keys():
        try:
            df = pd.read_excel(DATA_DIR / 'historical/all-euro-data-2025-2026.xlsx', sheet_name=sheet)
            df['Date'] = pd.to_datetime(df['Date'], errors='coerce')
            df['League'] = sheet
            df = df[df['FTR'].notna()]
            all_matches.append(df)
            print(f"   {sheet}: {len(df)} matches")
        except: pass
    
    combined = pd.concat(all_matches, ignore_index=True)
    combined.to_pickle(CACHE_FILE)
    print(f"   ✅ Cached {len(combined)} matches")
    return combined

def run_backtest():
    """Run backtest on 150+ matches."""
    print("=" * 60)
    print("🔬 FAST BACKTEST (NO GEMINI)")
    print("   Models: ML + Poisson + Monte Carlo")
    print("=" * 60)
    
    # Load data
    stats_cache = load_team_stats_cache()
    print(f"   Cached {len(stats_cache)} teams")
    
    matches = load_matches()
    matches = matches[matches['Date'] >= datetime.now() - timedelta(weeks=12)]
    print(f"   Recent matches: {len(matches)}")
    
    # Load models
    from app.core.ml_predictor import get_ml_predictor
    from app.core.poisson import PoissonPredictor
    ml = get_ml_predictor()
    poisson = PoissonPredictor()
    
    # Test matches
    results = {'total': 0, 'ml_correct': 0, 'poisson_correct': 0}
    league_stats = {}
    
    print("\n🔄 Testing matches...")
    for _, row in matches.iterrows():
        if results['total'] >= 150:
            break
        
        lid = row['League']
        home_stats = stats_cache.get(f"{lid}:{row['HomeTeam'].lower()}", {})
        away_stats = stats_cache.get(f"{lid}:{row['AwayTeam'].lower()}", {})
        
        if not home_stats or not away_stats:
            continue
        
        # ML prediction
        odds = {'home': float(row.get('B365H', 2.0) or 2.0), 'draw': float(row.get('B365D', 3.3) or 3.3), 'away': float(row.get('B365A', 3.5) or 3.5)}
        ml_pred = ml.predict(home_stats, away_stats, odds, {}).get('prediction', 'H')
        
        # Poisson prediction
        goals = home_stats.get('goals', {})
        home_attack = float(goals.get('for', {}).get('average', {}).get('total', '1.3') or '1.3')
        home_defense = float(goals.get('against', {}).get('average', {}).get('total', '1.1') or '1.1')
        goals = away_stats.get('goals', {})
        away_attack = float(goals.get('for', {}).get('average', {}).get('total', '1.1') or '1.1')
        away_defense = float(goals.get('against', {}).get('average', {}).get('total', '1.3') or '1.3')
        poisson_pred = poisson.predict(home_attack, away_attack, home_defense, away_defense).get('hdw', 'H')
        
        actual = row['FTR']
        
        results['total'] += 1
        if ml_pred == actual:
            results['ml_correct'] += 1
        if poisson_pred == actual:
            results['poisson_correct'] += 1
        
        if lid not in league_stats:
            league_stats[lid] = {'total': 0, 'ml_correct': 0}
        league_stats[lid]['total'] += 1
        if ml_pred == actual:
            league_stats[lid]['ml_correct'] += 1
    
    # Print results
    t = results['total']
    print(f"\n{'=' * 60}")
    print("📊 BACKTEST RESULTS")
    print(f"{'=' * 60}")
    print(f"\n📈 OVERALL ({t} matches)")
    print(f"   ML Model:  {results['ml_correct']:3}/{t} = {results['ml_correct']/t*100:5.1f}%")
    print(f"   Poisson:   {results['poisson_correct']:3}/{t} = {results['poisson_correct']/t*100:5.1f}%")
    print(f"\n📋 BY LEAGUE")
    for lid, s in sorted(league_stats.items(), key=lambda x: -x[1]['total']):
        name = LEAGUE_NAMES.get(lid, lid)
        acc = s['ml_correct'] / s['total'] * 100
        print(f"   {name:20} {s['ml_correct']:3}/{s['total']:3} = {acc:5.1f}%")
    print(f"{'=' * 60}")

if __name__ == "__main__":
    run_backtest()
