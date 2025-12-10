"""
Backtest Analysis Pipeline WITHOUT Gemini - 150+ Matches
Tests: ML Model + Poisson + Monte Carlo
Generates accuracy report overall and by league.
"""
import pandas as pd
import json
from pathlib import Path
from datetime import datetime, timedelta
from typing import Dict
import warnings
warnings.filterwarnings('ignore')

# Add project root
import sys
PROJECT_ROOT = Path(__file__).parent.parent
sys.path.insert(0, str(PROJECT_ROOT))

from app.core.poisson import PoissonPredictor
from app.core.monte_carlo import MonteCarloSimulator
from app.core.ml_predictor import get_ml_predictor

DATA_DIR = PROJECT_ROOT / "data"
REPORT_DIR = PROJECT_ROOT.parent / ".gemini/antigravity/brain/b1985c48-1be7-4eb1-adb4-3aa610621c58"

LEAGUE_FOLDERS = {
    'E0': 'Premier_League', 'E1': 'Championship', 'E2': 'League_One', 'E3': 'League_Two',
    'D1': 'Bundesliga', 'D2': '2_Bundesliga',
    'I1': 'Serie_A', 'I2': 'Serie_B',
    'F1': 'Ligue_1', 'F2': 'Ligue_2',
    'SP1': 'La_Liga'
}

LEAGUE_NAMES = {
    'E0': 'Premier League', 'E1': 'Championship', 'E2': 'League One', 'E3': 'League Two',
    'D1': 'Bundesliga', 'D2': '2. Bundesliga',
    'I1': 'Serie A', 'I2': 'Serie B',
    'F1': 'Ligue 1', 'F2': 'Ligue 2',
    'SP1': 'La Liga'
}


def load_raw_team_stats_cache() -> Dict[str, Dict]:
    """Load raw Football API JSON team stats into cache."""
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
                if team_name:
                    cache[team_name.lower()] = data
                    cache[f"{league_id}:{team_name.lower()}"] = data
            except Exception:
                continue
    
    return cache


def get_raw_team_stats(team_name: str, league_id: str, cache: Dict) -> Dict:
    """Get raw team stats with fuzzy matching."""
    key = f"{league_id}:{team_name.lower()}"
    if key in cache:
        return cache[key]
    
    if team_name.lower() in cache:
        return cache[team_name.lower()]
    
    for cached_name, data in cache.items():
        if team_name.lower() in cached_name or cached_name in team_name.lower():
            return data
    
    return {}


def analyze_match(row, ml_predictor, poisson, mc, stats_cache):
    """Analyze a single match using ML + Poisson + Monte Carlo."""
    home_team = row['HomeTeam']
    away_team = row['AwayTeam']
    league_id = row['League']
    
    # Get raw team stats
    home_stats = get_raw_team_stats(home_team, league_id, stats_cache)
    away_stats = get_raw_team_stats(away_team, league_id, stats_cache)
    
    if not home_stats or not away_stats:
        return None
    
    # Get odds
    odds = {
        'home': float(row.get('B365H', 2.0) or 2.0),
        'draw': float(row.get('B365D', 3.3) or 3.3),
        'away': float(row.get('B365A', 3.5) or 3.5)
    }
    
    # ML prediction
    ml_result = ml_predictor.predict(home_stats, away_stats, odds, {})
    
    # Extract attack/defense for Poisson/MC
    goals = home_stats.get('goals', {})
    home_attack = float(goals.get('for', {}).get('average', {}).get('total', '1.3') or '1.3')
    home_defense = float(goals.get('against', {}).get('average', {}).get('total', '1.1') or '1.1')
    
    goals = away_stats.get('goals', {})
    away_attack = float(goals.get('for', {}).get('average', {}).get('total', '1.1') or '1.1')
    away_defense = float(goals.get('against', {}).get('average', {}).get('total', '1.3') or '1.3')
    
    # Poisson prediction
    poisson_result = poisson.predict(home_attack, away_attack, home_defense, away_defense)
    
    # Monte Carlo prediction
    mc_result = mc.simulate(home_attack, away_attack, home_defense, away_defense)
    
    # Consensus
    predictions = [
        ml_result.get('prediction', 'H'),
        poisson_result.get('hdw', 'H'),
        mc_result.get('hdw', 'H')
    ]
    pred_counts = {}
    for p in predictions:
        pred_counts[p] = pred_counts.get(p, 0) + 1
    
    max_agreement = max(pred_counts.values())
    consensus_pred = max(pred_counts, key=pred_counts.get)
    
    return {
        'home_team': home_team,
        'away_team': away_team,
        'league_id': league_id,
        'league_name': LEAGUE_NAMES.get(league_id, league_id),
        'date': str(row['Date'].date()) if pd.notna(row['Date']) else '',
        'ml_prediction': ml_result.get('prediction', 'H'),
        'ml_confidence': ml_result.get('confidence', 0.5),
        'poisson_prediction': poisson_result.get('hdw', 'H'),
        'mc_prediction': mc_result.get('hdw', 'H'),
        'consensus_prediction': consensus_pred,
        'agreement': max_agreement,
        'actual_result': row.get('FTR', '')
    }


def run_backtest(min_matches: int = 150):
    """Run backtest on 150+ matches."""
    print("=" * 70)
    print("🔬 ANALYSIS ENDPOINT BACKTEST (NO GEMINI)")
    print("   Models: ML (XGBoost + H2H) + Poisson + Monte Carlo")
    print("=" * 70)
    
    # Load team stats
    print("\n📥 Loading team stats from JSON files...")
    stats_cache = load_raw_team_stats_cache()
    print(f"   Cached {len(stats_cache)} team entries")
    
    # Load historical matches
    print("\n📂 Loading historical matches...")
    all_matches = []
    for sheet in LEAGUE_FOLDERS.keys():
        try:
            df = pd.read_excel(DATA_DIR / 'historical/all-euro-data-2025-2026.xlsx', sheet_name=sheet)
            df['Date'] = pd.to_datetime(df['Date'], errors='coerce')
            df['League'] = sheet
            df = df[df['FTR'].notna()]
            df = df[df['Date'] >= datetime.now() - timedelta(weeks=12)]  # Last 12 weeks
            all_matches.append(df)
        except Exception:
            pass
    
    historical = pd.concat(all_matches, ignore_index=True)
    historical = historical.sort_values('Date', ascending=False)
    print(f"   Loaded {len(historical)} recent matches")
    
    # Initialize predictors
    print("\n🔧 Loading models...")
    ml_predictor = get_ml_predictor()
    poisson = PoissonPredictor()
    mc = MonteCarloSimulator()
    
    # Analyze matches
    print(f"\n🔄 Analyzing {min_matches}+ matches...")
    results = []
    
    for _, row in historical.iterrows():
        if len(results) >= min_matches + 50:  # Get extra for buffer
            break
        
        analysis = analyze_match(row, ml_predictor, poisson, mc, stats_cache)
        if analysis:
            results.append(analysis)
    
    print(f"   ✅ Analyzed {len(results)} matches")
    
    # Calculate accuracy
    print("\n📊 Calculating accuracy...")
    
    # Overall
    correct_ml = sum(1 for r in results if r['ml_prediction'] == r['actual_result'])
    correct_poisson = sum(1 for r in results if r['poisson_prediction'] == r['actual_result'])
    correct_mc = sum(1 for r in results if r['mc_prediction'] == r['actual_result'])
    correct_consensus = sum(1 for r in results if r['consensus_prediction'] == r['actual_result'])
    total = len(results)
    
    # By league
    league_stats = {}
    for r in results:
        lid = r['league_id']
        if lid not in league_stats:
            league_stats[lid] = {'total': 0, 'ml_correct': 0, 'poisson_correct': 0, 'consensus_correct': 0}
        
        league_stats[lid]['total'] += 1
        if r['ml_prediction'] == r['actual_result']:
            league_stats[lid]['ml_correct'] += 1
        if r['poisson_prediction'] == r['actual_result']:
            league_stats[lid]['poisson_correct'] += 1
        if r['consensus_prediction'] == r['actual_result']:
            league_stats[lid]['consensus_correct'] += 1
    
    # Print results
    print("\n" + "=" * 70)
    print("📈 BACKTEST RESULTS")
    print("=" * 70)
    print(f"\n📊 OVERALL ({total} matches)")
    print("-" * 50)
    print(f"   ML Model:     {correct_ml:3}/{total} = {correct_ml/total*100:5.1f}%")
    print(f"   Poisson:      {correct_poisson:3}/{total} = {correct_poisson/total*100:5.1f}%")
    print(f"   Monte Carlo:  {correct_mc:3}/{total} = {correct_mc/total*100:5.1f}%")
    print(f"   Consensus:    {correct_consensus:3}/{total} = {correct_consensus/total*100:5.1f}%")
    
    print(f"\n📋 BY LEAGUE")
    print("-" * 50)
    for lid, stats in sorted(league_stats.items(), key=lambda x: -x[1]['total']):
        name = LEAGUE_NAMES.get(lid, lid)
        t = stats['total']
        ml_acc = stats['ml_correct'] / t * 100
        print(f"   {name:20} {stats['ml_correct']:3}/{t:3} = {ml_acc:5.1f}%")
    
    print("=" * 70)
    
    # Generate report
    report = f"""# Analysis Endpoint Backtest Report

**Date:** {datetime.now().strftime('%Y-%m-%d %H:%M')}  
**Matches Tested:** {total}  
**Models:** ML (XGBoost + H2H) + Poisson + Monte Carlo (NO Gemini)

---

## Overall Accuracy

| Model | Correct | Total | Accuracy |
|-------|---------|-------|----------|
| **ML Model** | {correct_ml} | {total} | **{correct_ml/total*100:.1f}%** |
| Poisson | {correct_poisson} | {total} | {correct_poisson/total*100:.1f}% |
| Monte Carlo | {correct_mc} | {total} | {correct_mc/total*100:.1f}% |
| Consensus | {correct_consensus} | {total} | {correct_consensus/total*100:.1f}% |

---

## Accuracy by League

| League | Matches | ML Correct | ML Accuracy |
|--------|---------|------------|-------------|
"""
    for lid, stats in sorted(league_stats.items(), key=lambda x: -x[1]['total']):
        name = LEAGUE_NAMES.get(lid, lid)
        t = stats['total']
        ml_acc = stats['ml_correct'] / t * 100
        report += f"| {name} | {t} | {stats['ml_correct']} | {ml_acc:.1f}% |\n"
    
    report += f"""
---

## Model Details

- **ML Model:** XGBoost with 59 features (team stats + H2H + odds)
- **Training Data:** 1,686 matches from 2025-2026 season
- **H2H Data:** 10,075 pairs from 7 historical seasons
- **Team Stats:** Football API JSON (672 teams cached)

## Notes

- Backtest uses the same analysis logic as `/api/v1/analyze` endpoint
- Gemini AI analysis was **not included** in this backtest
- Results reflect predictions on completed matches from last 12 weeks
"""
    
    # Save report
    report_path = REPORT_DIR / "backtest_report.md"
    with open(report_path, 'w') as f:
        f.write(report)
    
    print(f"\n📄 Report saved to: {report_path}")
    
    return results


if __name__ == "__main__":
    run_backtest(150)
