"""
Backtest Script with Full Analysis Pipeline
Runs predictions on historical matches using:
- Team Stats from JSON files
- ML Model (XGBoost) 
- Poisson Model
- Monte Carlo Simulation
- Gemini AI Analysis (optional, if API key available)

Uses REAL historical data from Excel files, hiding actual scores.
"""
import asyncio
import json
import pandas as pd
from pathlib import Path
from datetime import datetime, timedelta
from typing import Dict, List
import sys

# Add project root to path
PROJECT_ROOT = Path(__file__).parent.parent
sys.path.insert(0, str(PROJECT_ROOT))

# Load environment
from dotenv import load_dotenv
load_dotenv(PROJECT_ROOT / ".env")

from app.core.poisson import PoissonPredictor
from app.core.monte_carlo import MonteCarloSimulator
from app.core.ml_predictor import get_ml_predictor
from app.services.team_stats import get_team_stats_service
from app.services.gemini_service import get_gemini_service


DATA_DIR = PROJECT_ROOT / "data"

# League mapping
LEAGUE_NAMES = {
    'E0': 'Premier League', 'E1': 'Championship', 'E2': 'League One', 'E3': 'League Two',
    'D1': 'Bundesliga', 'D2': '2. Bundesliga',
    'I1': 'Serie A', 'I2': 'Serie B',
    'F1': 'Ligue 1', 'F2': 'Ligue 2',
    'SP1': 'La Liga'
}


def load_historical_matches(weeks: int = 9) -> pd.DataFrame:
    """Load REAL historical matches from Excel files."""
    historical_dir = DATA_DIR / "historical"
    
    excel_files = sorted(historical_dir.glob("*.xlsx"), reverse=True)
    if not excel_files:
        print("❌ No historical Excel files found")
        return pd.DataFrame()
    
    current_season_file = excel_files[0]
    print(f"📂 Loading: {current_season_file.name}")
    
    all_matches = []
    xl = pd.ExcelFile(current_season_file)
    
    end_date = datetime.now()
    start_date = end_date - timedelta(weeks=weeks)
    
    print(f"📅 Period: {start_date.strftime('%Y-%m-%d')} → {end_date.strftime('%Y-%m-%d')}")
    
    for sheet_name in xl.sheet_names:
        if sheet_name not in LEAGUE_NAMES:
            continue
        
        df = pd.read_excel(current_season_file, sheet_name=sheet_name)
        
        if 'Date' not in df.columns:
            continue
        
        df['Date'] = pd.to_datetime(df['Date'], errors='coerce')
        df['League'] = sheet_name
        
        df = df[(df['Date'] >= start_date) & (df['Date'] <= end_date)]
        
        if 'FTR' in df.columns:
            df = df[df['FTR'].notna()]
            all_matches.append(df)
    
    if not all_matches:
        return pd.DataFrame()
    
    combined = pd.concat(all_matches, ignore_index=True)
    print(f"✅ Loaded {len(combined)} matches from {len(set(combined['League']))} leagues\n")
    return combined


# League folder mapping for JSON files
LEAGUE_FOLDERS = {
    'E0': 'Premier_League', 'E1': 'Championship', 'E2': 'League_One', 'E3': 'League_Two',
    'D1': 'Bundesliga', 'D2': '2_Bundesliga',
    'I1': 'Serie_A', 'I2': 'Serie_B',
    'F1': 'Ligue_1', 'F2': 'Ligue_2',
    'SP1': 'La_Liga'
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
    """Get raw Football API team stats with fuzzy matching."""
    key = f"{league_id}:{team_name.lower()}"
    if key in cache:
        return cache[key]
    
    if team_name.lower() in cache:
        return cache[team_name.lower()]
    
    # Fuzzy match
    for cached_name, data in cache.items():
        if team_name.lower() in cached_name or cached_name in team_name.lower():
            return data
    
    return {}


def build_h2h_cache(historical_df: pd.DataFrame) -> Dict:
    """Build H2H statistics cache from historical data."""
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


def get_h2h_features(home_team: str, away_team: str, h2h_cache: Dict) -> Dict:
    """Get H2H features for ML prediction."""
    key = f"{home_team.lower()}:{away_team.lower()}"
    h2h = h2h_cache.get(key, {})
    
    matches = h2h.get('matches', 0)
    if matches == 0:
        return {}  # Use defaults
    
    return {
        'matches': matches,
        'home_wins': h2h.get('home_wins', 0),
        'draws': h2h.get('draws', 0),
        'away_wins': h2h.get('away_wins', 0),
        'home_win_rate': h2h.get('home_wins', 0) / matches,
        'draw_rate': h2h.get('draws', 0) / matches,
        'away_win_rate': h2h.get('away_wins', 0) / matches,
        'avg_goals': h2h.get('total_goals', 0) / matches,
        'btts_rate': h2h.get('btts_count', 0) / matches,
        'over25_rate': h2h.get('over25_count', 0) / matches
    }


def analyze_match_full(row: pd.Series, ml_predictor, poisson: PoissonPredictor, 
                        mc: MonteCarloSimulator, raw_stats_cache: Dict, h2h_cache: Dict) -> Dict:
    """
    Full analysis pipeline for a single match.
    Uses: ML Model (raw JSON stats), Poisson, Monte Carlo (Gemini is batch).
    """
    home_team = row['HomeTeam']
    away_team = row['AwayTeam']
    league_id = row['League']
    
    # Get RAW Football API team stats for ML model
    home_stats_raw = get_raw_team_stats(home_team, league_id, raw_stats_cache)
    away_stats_raw = get_raw_team_stats(away_team, league_id, raw_stats_cache)
    
    # Get H2H features for ML model
    h2h_features = get_h2h_features(home_team, away_team, h2h_cache)
    
    # Extract attack/defense for Poisson/MC from raw stats
    goals = home_stats_raw.get('goals', {})
    home_attack = float(goals.get('for', {}).get('average', {}).get('total', '1.3') or '1.3')
    home_defense = float(goals.get('against', {}).get('average', {}).get('total', '1.1') or '1.1')
    
    goals = away_stats_raw.get('goals', {})
    away_attack = float(goals.get('for', {}).get('average', {}).get('total', '1.1') or '1.1')
    away_defense = float(goals.get('against', {}).get('average', {}).get('total', '1.3') or '1.3')
    
    # Get odds if available
    odds = {
        'home': row.get('B365H', row.get('PSH', 2.0)) or 2.0,
        'draw': row.get('B365D', row.get('PSD', 3.3)) or 3.3,
        'away': row.get('B365A', row.get('PSA', 3.5)) or 3.5
    }
    
    # ML prediction with RAW stats + H2H
    ml_result = ml_predictor.predict(home_stats_raw, away_stats_raw, odds, h2h_features)
    
    # Poisson prediction
    poisson_result = poisson.predict(home_attack, away_attack, home_defense, away_defense)
    
    # Monte Carlo prediction
    mc_result = mc.simulate(home_attack, away_attack, home_defense, away_defense)
    
    # Calculate consensus (3 models: ML, Poisson, MC)
    predictions = [
        ml_result.get('prediction', 'H'),
        poisson_result.get('prediction', 'H'),
        mc_result.get('prediction', 'H')
    ]
    
    # Count agreement
    pred_counts = {}
    for p in predictions:
        pred_counts[p] = pred_counts.get(p, 0) + 1
    
    # Determine consensus level
    max_agreement = max(pred_counts.values())
    if max_agreement == 3:
        pattern = "STRONG_CONSENSUS"
        consensus_pred = predictions[0]  # All agree
    elif max_agreement == 2:
        pattern = "PARTIAL_CONSENSUS"
        consensus_pred = max(pred_counts, key=pred_counts.get)  # Majority
    else:
        pattern = "DIVERGENT"
        consensus_pred = ml_result.get('prediction', 'H')  # Default to ML
    
    return {
        'home_team': home_team,
        'away_team': away_team,
        'league': LEAGUE_NAMES.get(league_id, league_id),
        'league_id': league_id,
        'date': str(row['Date'].date()) if pd.notna(row['Date']) else '',
        'team_stats': {'home': home_stats_raw, 'away': away_stats_raw},
        'odds': odds,
        'ml_analysis': {
            'prediction': ml_result.get('prediction', 'H'),
            'confidence': ml_result.get('confidence', 0.5),
            'home_win': ml_result.get('home_win', 0.33),
            'draw': ml_result.get('draw', 0.33),
            'away_win': ml_result.get('away_win', 0.33)
        },
        'poisson_analysis': {
            'prediction': poisson_result.get('prediction', 'H'),
            'home_win': poisson_result.get('home_win', 0.33),
            'draw': poisson_result.get('draw', 0.33),
            'away_win': poisson_result.get('away_win', 0.33)
        },
        'monte_carlo_analysis': {
            'prediction': mc_result.get('prediction', 'H'),
            'home_win': mc_result.get('home_win', 0.33),
            'draw': mc_result.get('draw', 0.33),
            'away_win': mc_result.get('away_win', 0.33)
        },
        'pattern_analysis': {
            'pattern': pattern,
            'consensus_prediction': consensus_pred,
            'agreement': f"{max_agreement}/3"
        },
        'ml_predictions': {  # For compatibility
            'hdw': {
                'prediction': ml_result.get('prediction', 'H'),
                'confidence': ml_result.get('confidence', 0.5)
            }
        },
        'actual_result': row.get('FTR', '')
    }


async def run_backtest_with_gemini(weeks: int = 9, use_gemini: bool = True):
    """Run full backtest with ML + Poisson + Monte Carlo + Gemini."""
    print("=" * 70)
    print(f"🔬 BACKTEST: Full Analysis Pipeline ({weeks} weeks)")
    print("   Methods: ML Model (59 features) + Poisson + Monte Carlo + Gemini AI")
    print("=" * 70)
    
    # Load REAL historical matches
    matches_df = load_historical_matches(weeks)
    
    if matches_df.empty:
        print("❌ No matches to backtest")
        return
    
    # Load RAW Football API team stats cache
    print("📥 Loading raw team stats from JSON files...")
    raw_stats_cache = load_raw_team_stats_cache()
    print(f"   Cached {len(raw_stats_cache)} team entries")
    
    # Load H2H cache from all historical seasons
    print("📊 Building H2H cache from historical data...")
    historical_dir = DATA_DIR / "historical"
    h2h_matches = []
    for excel_file in sorted(historical_dir.glob("*.xlsx")):
        try:
            xl = pd.ExcelFile(excel_file)
            for sheet_name in xl.sheet_names:
                if sheet_name in LEAGUE_FOLDERS:
                    df = pd.read_excel(excel_file, sheet_name=sheet_name)
                    if 'FTR' in df.columns:
                        df = df[df['FTR'].notna()]
                        h2h_matches.append(df)
        except Exception:
            continue
    
    if h2h_matches:
        h2h_combined = pd.concat(h2h_matches, ignore_index=True)
        h2h_cache = build_h2h_cache(h2h_combined)
        print(f"   Built {len(h2h_cache)} H2H pairs")
    else:
        h2h_cache = {}
    
    # Initialize ALL services
    print("🔧 Loading models...")
    ml_predictor = get_ml_predictor()
    poisson = PoissonPredictor()
    mc = MonteCarloSimulator()
    gemini = get_gemini_service()
    
    # Analyze all matches
    print("🔄 Running analysis on all matches...")
    all_analyses = []
    
    for idx, row in matches_df.iterrows():
        try:
            analysis = analyze_match_full(row, ml_predictor, poisson, mc, raw_stats_cache, h2h_cache)
            all_analyses.append(analysis)
        except Exception as e:
            if len(all_analyses) < 3:  # Show first few errors
                print(f"⚠️ Error: {row.get('HomeTeam', 'Unknown')} - {e}")
            continue
    
    print(f"✅ Analyzed {len(all_analyses)} matches")
    
    # Group by league for Gemini batch analysis
    if use_gemini and gemini.api_key:
        print("\n🤖 Running Gemini AI analysis by league...")
        leagues = {}
        for match in all_analyses:
            lid = match['league_id']
            if lid not in leagues:
                leagues[lid] = []
            leagues[lid].append(match)
        
        gemini_analyzed = []
        for league_id, league_matches in leagues.items():
            league_name = LEAGUE_NAMES.get(league_id, league_id)
            print(f"   → {league_name}: {len(league_matches)} matches")
            try:
                analyzed = await gemini.analyze_matches_batch(league_matches, league_name)
                gemini_analyzed.extend(analyzed)
            except Exception as e:
                print(f"     ⚠️ Gemini error: {e}")
                gemini_analyzed.extend(league_matches)
        
        all_analyses = gemini_analyzed
    
    # Calculate accuracy
    results = {
        'total': 0,
        'correct': 0,
        'consensus': {'total': 0, 'correct': 0},
        'divergent': {'total': 0, 'correct': 0},
        'gemini': {'total': 0, 'correct': 0},
        'by_league': {}
    }
    
    print("\n📊 Calculating accuracy...\n")
    
    for match in all_analyses:
        actual = match.get('actual_result', '')
        if not actual:
            continue
        
        # Statistical prediction (Poisson/MC consensus)
        stat_pred = match.get('poisson_analysis', {}).get('prediction', 'H')
        pattern = match.get('pattern_analysis', {}).get('pattern', 'DIVERGENT')
        
        # Gemini prediction if available
        gemini_pred = match.get('gemini_analysis', {}).get('prediction', stat_pred)
        
        # Use Gemini if available, else statistical
        final_pred = gemini_pred if match.get('gemini_analysis') else stat_pred
        correct = (final_pred == actual)
        
        results['total'] += 1
        if correct:
            results['correct'] += 1
        
        # Track by pattern
        if pattern == "CONSENSUS":
            results['consensus']['total'] += 1
            if correct:
                results['consensus']['correct'] += 1
        else:
            results['divergent']['total'] += 1
            if correct:
                results['divergent']['correct'] += 1
        
        # Track Gemini accuracy
        if match.get('gemini_analysis'):
            results['gemini']['total'] += 1
            if gemini_pred == actual:
                results['gemini']['correct'] += 1
        
        # Track by league
        league_id = match.get('league_id', 'unknown')
        if league_id not in results['by_league']:
            results['by_league'][league_id] = {'total': 0, 'correct': 0}
        results['by_league'][league_id]['total'] += 1
        if correct:
            results['by_league'][league_id]['correct'] += 1
    
    # Print results
    print("=" * 70)
    print("📈 BACKTEST RESULTS")
    print("=" * 70)
    
    if results['total'] > 0:
        acc = results['correct'] / results['total'] * 100
        print(f"\n📊 Overall: {results['correct']}/{results['total']} = {acc:.1f}%")
    
    if results['consensus']['total'] > 0:
        acc = results['consensus']['correct'] / results['consensus']['total'] * 100
        print(f"✅ Consensus (Poisson+MC agree): {results['consensus']['correct']}/{results['consensus']['total']} = {acc:.1f}%")
    
    if results['divergent']['total'] > 0:
        acc = results['divergent']['correct'] / results['divergent']['total'] * 100
        print(f"⚠️  Divergent (models disagree): {results['divergent']['correct']}/{results['divergent']['total']} = {acc:.1f}%")
    
    if results['gemini']['total'] > 0:
        acc = results['gemini']['correct'] / results['gemini']['total'] * 100
        print(f"🤖 Gemini AI: {results['gemini']['correct']}/{results['gemini']['total']} = {acc:.1f}%")
    
    print("\n📋 By League:")
    print("-" * 50)
    for league_id, stats in sorted(results['by_league'].items(), 
                                     key=lambda x: x[1]['total'], reverse=True):
        if stats['total'] > 0:
            acc = stats['correct'] / stats['total'] * 100
            league_name = LEAGUE_NAMES.get(league_id, league_id)
            print(f"  {league_name:25} {stats['correct']:4}/{stats['total']:4} = {acc:5.1f}%")
    
    print("\n" + "=" * 70)
    print(f"✅ Backtest complete: {results['total']} matches analyzed")
    print("=" * 70)
    
    return results


def main():
    import argparse
    parser = argparse.ArgumentParser(description="Run backtest with full analysis pipeline")
    parser.add_argument("--weeks", type=int, default=9, help="Number of weeks to backtest")
    parser.add_argument("--no-gemini", action="store_true", help="Skip Gemini AI analysis")
    args = parser.parse_args()
    
    asyncio.run(run_backtest_with_gemini(args.weeks, use_gemini=not args.no_gemini))


if __name__ == "__main__":
    main()
