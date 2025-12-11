"""
Simplified Backtest: ML Model + Trap Detector + Gemini Only
No Poisson, No Monte Carlo - Let Gemini decide based on ML predictions and trap warnings
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

from app.core.ml_predictor import get_ml_predictor
from app.core.trap_detector import TrapDetector
from app.services.gemini_service import get_gemini_service

DATA_DIR = PROJECT_ROOT / "data"
REPORT_DIR = Path.home() / ".gemini" / "antigravity" / "brain" / "3d63d768-8366-4689-9f50-05ba8a1be4a6"
REPORT_DIR.mkdir(parents=True, exist_ok=True)

# League mapping
LEAGUE_NAMES = {
    'E0': 'Premier League', 'E1': 'Championship', 'E2': 'League One', 'E3': 'League Two',
    'D1': 'Bundesliga', 'D2': '2. Bundesliga',
    'I1': 'Serie A', 'I2': 'Serie B',
    'F1': 'Ligue 1', 'F2': 'Ligue 2',
    'SP1': 'La Liga'
}


def load_historical_matches(weeks: int = 10) -> pd.DataFrame:
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


def calculate_historical_stats(team_name: str, league_id: str, before_date: datetime, all_historical_df: pd.DataFrame) -> Dict:
    """Calculate team stats from Excel data ONLY using matches before the target date."""
    team_matches = all_historical_df[
        ((all_historical_df['HomeTeam'] == team_name) | (all_historical_df['AwayTeam'] == team_name)) &
        (all_historical_df['League'] == league_id) &
        (all_historical_df['Date'] < before_date)
    ].sort_values('Date')
    
    if len(team_matches) == 0:
        return {
            'avg_goals_scored': 1.3,
            'avg_goals_conceded': 1.2,
            'form': [],
            'goals': {'for': {'average': {'total': '1.3'}}, 'against': {'average': {'total': '1.2'}}}
        }
    
    recent = team_matches.tail(10)
    
    goals_scored = []
    goals_conceded = []
    form = []
    clean_sheets = 0
    
    for _, match in recent.iterrows():
        is_home = match['HomeTeam'] == team_name
        
        if is_home:
            gf = match.get('FTHG', 0) or 0
            ga = match.get('FTAG', 0) or 0
            result = match.get('FTR', '')
        else:
            gf = match.get('FTAG', 0) or 0
            ga = match.get('FTHG', 0) or 0
            ftr = match.get('FTR', '')
            result = 'A' if ftr == 'H' else ('H' if ftr == 'A' else 'D')
        
        goals_scored.append(gf)
        goals_conceded.append(ga)
        
        if ga == 0:
            clean_sheets += 1
        
        if result == 'H' if is_home else result == 'A':
            form.append('W')
        elif result == 'D':
            form.append('D')
        else:
            form.append('L')
    
    avg_scored = sum(goals_scored) / len(goals_scored) if goals_scored else 1.3
    avg_conceded = sum(goals_conceded) / len(goals_conceded) if goals_conceded else 1.2
    
    return {
        'avg_goals_scored': round(avg_scored, 2),
        'avg_goals_conceded': round(avg_conceded, 2),
        'form': form[-5:],
        'clean_sheets': clean_sheets,
        'goals': {
            'for': {'average': {'total': str(round(avg_scored, 1))}},
            'against': {'average': {'total': str(round(avg_conceded, 1))}}
        }
    }


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
                'total_goals': 0, 'btts_count': 0, 'over25_count': 0, 'under_2_count': 0
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
        
        if fthg + ftag < 2:
            h2h['under_2_count'] += 1
    
    return h2h_cache


def get_h2h_features(home_team: str, away_team: str, h2h_cache: Dict) -> Dict:
    """Get H2H features for ML prediction."""
    key = f"{home_team.lower()}:{away_team.lower()}"
    h2h = h2h_cache.get(key, {})
    
    matches = h2h.get('matches', 0)
    if matches == 0:
        return {'under_2_rate': 0, 'draw_rate': 0}
    
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
        'over25_rate': h2h.get('over25_count', 0) / matches,
        'under_2_rate': h2h.get('under_2_count', 0) / matches
    }


async def run_simplified_backtest(weeks: int = 10):
    """Run simplified backtest: ML + Trap + Gemini only."""
    print("=" * 70)
    print(f"🎯 SIMPLIFIED BACKTEST: ML Model + Trap Detector + Gemini Only")
    print(f"   Period: {weeks} weeks")
    print(f"   NO Poisson, NO Monte Carlo - Gemini decides!")
    print("=" * 70)
    
    # Load historical matches
    matches_df = load_historical_matches(weeks)
    
    if matches_df.empty:
        print("❌ No matches to backtest")
        return
    
    # Load ALL historical data for stats calculation
    print("📊 Loading complete historical dataset...")
    historical_dir = DATA_DIR / "historical"
    all_historical = []
    for excel_file in sorted(historical_dir.glob("*.xlsx")):
        try:
            xl = pd.ExcelFile(excel_file)
            for sheet_name in xl.sheet_names:
                if sheet_name in LEAGUE_NAMES:
                    df = pd.read_excel(excel_file, sheet_name=sheet_name)
                    if 'FTR' in df.columns:
                        df['Date'] = pd.to_datetime(df['Date'], errors='coerce')
                        df['League'] = sheet_name
                        df = df[df['FTR'].notna()]
                        all_historical.append(df)
        except Exception:
            continue
    
    all_historical_df = pd.concat(all_historical, ignore_index=True) if all_historical else pd.DataFrame()
    print(f"   Loaded {len(all_historical_df)} total historical matches")
    
    # Build H2H cache
    print("📊 Building H2H cache...")
    h2h_cache = build_h2h_cache(all_historical_df)
    print(f"   Built {len(h2h_cache)} H2H pairs")
    
    # Initialize services (ML + Trap + Gemini ONLY)
    print("🔧 Loading models...")
    ml_predictor = get_ml_predictor()
    trap_detector = TrapDetector()
    gemini = get_gemini_service()
    print("   ✅ ML Model")
    print("   ✅ Trap Detector")
    print("   ✅ Gemini AI")
    print("   ❌ Poisson (skipped)")
    print("   ❌ Monte Carlo (skipped)")
    
    # Analyze all matches
    print("\n🔄 Running simplified analysis...\n")
    all_analyses = []
    
    for idx, row in matches_df.iterrows():
        try:
            home_team = row['HomeTeam']
            away_team = row['AwayTeam']
            league_id = row['League']
            match_date = row['Date']
            
            # Calculate stats from Excel data ONLY
            home_stats = calculate_historical_stats(home_team, league_id, match_date, all_historical_df)
            away_stats = calculate_historical_stats(away_team, league_id, match_date, all_historical_df)
            
            # Get H2H features
            h2h_features = get_h2h_features(home_team, away_team, h2h_cache)
            
            # Get odds
            odds = {
                'home': row.get('B365H', row.get('PSH', 2.0)) or 2.0,
                'draw': row.get('B365D', row.get('PSD', 3.3)) or 3.3,
                'away': row.get('B365A', row.get('PSA', 3.5)) or 3.5
            }
            
            # ML prediction ONLY
            ml_result = ml_predictor.predict(home_stats, away_stats, odds, h2h_features)
            
            # Trap detection
            trap_result = trap_detector.detect({
                'home_stats': home_stats,
                'away_stats': away_stats,
                'h2h': h2h_features,
                'odds': odds
            })
            
            # Store actual goals
            actual_fthg = row.get('FTHG', 0) or 0
            actual_ftag = row.get('FTAG', 0) or 0
            
            analysis = {
                'home_team': home_team,
                'away_team': away_team,
                'league': LEAGUE_NAMES.get(league_id, league_id),
                'league_id': league_id,
                'date': str(match_date.date()) if pd.notna(match_date) else '',
                'team_stats': {'home': home_stats, 'away': away_stats},
                'odds': odds,
                'ml_analysis': {
                    'prediction': ml_result.get('prediction', 'H'),
                    'confidence': ml_result.get('confidence', 0.5),
                    'home_win': ml_result.get('home_win', 0.33),
                    'draw': ml_result.get('draw', 0.33),
                    'away_win': ml_result.get('away_win', 0.33)
                },
                'trap_detector': trap_result,
                'actual_result': row.get('FTR', ''),
                'fthg': actual_fthg,
                'ftag': actual_ftag
            }
            
            all_analyses.append(analysis)
            
        except Exception as e:
            if len(all_analyses) < 3:
                print(f"⚠️ Error: {row.get('HomeTeam', 'Unknown')} - {e}")
            continue
    
    print(f"✅ Analyzed {len(all_analyses)} matches (ML + Trap only)\n")
    
    # Gemini analysis - Let Gemini decide based on ML + Trap
    if gemini.api_key and gemini.api_key != "your_gemini_api_key_here":
        print("🤖 Running Gemini AI final analysis...")
        leagues = {}
        for match in all_analyses:
            lid = match['league_id']
            if lid not in leagues:
                leagues[lid] = []
            leagues[lid].append(match)
        
        gemini_analyzed = []
        tasks = []
        for league_id, league_matches in leagues.items():
            league_name = LEAGUE_NAMES.get(league_id, league_id)
            print(f"   → {league_name}: {len(league_matches)} matches (queued)")
            tasks.append(gemini.analyze_matches_batch(league_matches, league_name))
        
        try:
            results_list = await asyncio.gather(*tasks, return_exceptions=True)
            for i, league_result in enumerate(results_list):
                if isinstance(league_result, Exception):
                    league_id = list(leagues.keys())[i]
                    league_name = LEAGUE_NAMES.get(league_id, league_id)
                    print(f"     ⚠️ {league_name} error: {league_result}")
                    gemini_analyzed.extend(leagues[league_id])
                else:
                    gemini_analyzed.extend(league_result)
        except Exception as e:
            print(f"     ⚠️ Batch error: {e}")
            gemini_analyzed = all_analyses
        
        all_analyses = gemini_analyzed
        print()
    else:
        print("⚠️  No Gemini API key - cannot complete analysis\n")
        return
    
    # Generate report
    await generate_simplified_report(all_analyses, weeks)
    
    return all_analyses


async def generate_simplified_report(analyses: List[Dict], weeks: int):
    """Generate report for simplified backtest."""
    
    if not analyses:
        print("❌ No analyses to report")
        return
    
    # Calculate statistics
    total = len(analyses)
    ml_correct = 0
    gemini_correct = 0
    trap_detected = 0
    trap_correct = 0
    
    for match in analyses:
        actual = match.get('actual_result', '')
        if not actual:
            continue
        
        ml_pred = match.get('ml_analysis', {}).get('prediction')
        gemini_pred = match.get('gemini_analysis', {}).get('prediction')
        
        if ml_pred == actual:
            ml_correct += 1
        
        if gemini_pred == actual:
            gemini_correct += 1
        
        trap = match.get('trap_detector', {})
        if trap.get('is_trap'):
            trap_detected += 1
            if ml_pred != actual:
                trap_correct += 1
    
    ml_accuracy = (ml_correct / total * 100) if total > 0 else 0
    gemini_accuracy = (gemini_correct / total * 100) if total > 0 else 0
    trap_accuracy = (trap_correct / trap_detected * 100) if trap_detected > 0 else 0
    
    # Generate report
    report_path = REPORT_DIR / "simplified_backtest_report.md"
    
    with open(report_path, 'w') as f:
        f.write(f"# Simplified Backtest Report\n\n")
        f.write(f"**Scenario**: ML Model + Trap Detector + Gemini Only (No Poisson/Monte Carlo)\n\n")
        f.write(f"**Generated**: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n\n")
        
        f.write("## Executive Summary\n\n")
        f.write(f"- **Period**: {weeks} weeks\n")
        f.write(f"- **Total Matches**: {total}\n")
        f.write(f"- **ML Model Accuracy**: {ml_accuracy:.1f}%\n")
        f.write(f"- **Gemini AI Accuracy**: {gemini_accuracy:.1f}%\n")
        f.write(f"- **Traps Detected**: {trap_detected}\n")
        f.write(f"- **Trap Detection Accuracy**: {trap_accuracy:.1f}%\n\n")
        
        f.write("## Model Comparison\n\n")
        f.write("| Model | Accuracy | Correct | Total |\n")
        f.write("|-------|----------|---------|-------|\n")
        f.write(f"| ML Model (Input) | {ml_accuracy:.1f}% | {ml_correct}/{total} |\n")
        f.write(f"| Gemini AI (Final Decision) | {gemini_accuracy:.1f}% | {gemini_correct}/{total} |\n\n")
        
        f.write("## Trap Detector Performance\n\n")
        f.write(f"- **Total Traps Detected**: {trap_detected}\n")
        f.write(f"- **Correctly Flagged**: {trap_correct} ({trap_accuracy:.1f}% accuracy)\n")
        f.write(f"- **Money Saved**: €{trap_correct * 100:,.0f}\n\n")
        
        f.write("## Conclusion\n\n")
        if gemini_accuracy > ml_accuracy:
            diff =gemini_accuracy - ml_accuracy
            f.write(f"✅ **Gemini improved predictions by {diff:.1f}%** over ML Model alone\n\n")
        elif gemini_accuracy < ml_accuracy:
            diff = ml_accuracy - gemini_accuracy
            f.write(f"⚠️ **Gemini reduced accuracy by {diff:.1f}%** compared to ML Model\n\n")
        else:
            f.write(f"➡️ **Gemini matched ML Model performance** exactly\n\n")
        
        f.write("This simplified pipeline shows how Gemini performs when given only:\n")
        f.write("- Team statistics\n")
        f.write("- ML Model predictions & confidence\n")
        f.write("- Trap detector warnings\n")
    
    print(f"📄 Report generated: {report_path}")
    print(f"\n{'=' * 70}")
    print("✅ Simplified Backtest Complete!")
    print(f"{'=' * 70}\n")
    
    # Copy to project directory
    import shutil
    shutil.copy(report_path, PROJECT_ROOT / "simplified_backtest_report.md")
    print(f"📋 Report also saved to: {PROJECT_ROOT}/simplified_backtest_report.md\n")


def main():
    import argparse
    parser = argparse.ArgumentParser(description="Run simplified backtest: ML + Trap + Gemini only")
    parser.add_argument("--weeks", type=int, default=10, help="Number of weeks to backtest")
    args = parser.parse_args()
    
    asyncio.run(run_simplified_backtest(args.weeks))


if __name__ == "__main__":
    main()
