"""
Enhanced Backtest Script - 10 Weeks with Trap Detection & Ticket Analysis
Features:
- Excel-based stats calculation (no data leakage)
- Trap detector integration
- Ticket generation strategies
- Comprehensive reporting with ROI analysis
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
from app.core.poisson import PoissonPredictor
from app.core.monte_carlo import MonteCarloSimulator
from app.core.trap_detector import TrapDetector
from app.core.fixture_congestion import calculate_fixture_congestion
from app.core.derby_detector import get_derby_detector
from app.services.gemini_service import get_gemini_service

DATA_DIR = PROJECT_ROOT / "data"
REPORT_DIR = Path.home() / ".gemini" / "antigravity" / "brain" / "3d63d768-8366-4689-9f50-05ba8a1be4a6"
# Ensure report directory exists
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
    """
    Calculate team stats from Excel data ONLY using matches before the target date.
    This prevents data leakage.
    """
    # Filter to team's matches before this date
    team_matches = all_historical_df[
        ((all_historical_df['HomeTeam'] == team_name) | (all_historical_df['AwayTeam'] == team_name)) &
        (all_historical_df['League'] == league_id) &
        (all_historical_df['Date'] < before_date)
    ].sort_values('Date')
    
    if len(team_matches) == 0:
        # Return defaults if no historical data
        return {
            'avg_goals_scored': 1.3,
            'avg_goals_conceded': 1.2,
            'form': [],
            'goals': {'for': {'average': {'total': '1.3'}}, 'against': {'average': {'total': '1.2'}}}
        }
    
    # Use last 10 games for rolling stats
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
        
        # Form: W/D/L
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
        'form': form[-5:],  # Last 5 games
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


def load_all_historical_data() -> pd.DataFrame:
    """Load ALL historical data for stats calculation."""
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
    
    combined = pd.concat(all_historical, ignore_index=True) if all_historical else pd.DataFrame()
    print(f"   Loaded {len(combined)} total historical matches")
    return combined

async def run_enhanced_backtest(weeks: int = 10, use_gemini: bool = False):
    """Run comprehensive backtest with all features."""
    print("=" * 70)
    print(f"🔬 ENHANCED BACKTEST: 10-Week Analysis with Trap Detection")
    print(f"   Period: {weeks} weeks")
    print("=" * 70)
    
    # Load historical matches
    matches_df = load_historical_matches(weeks)
    
    if matches_df.empty:
        print("❌ No matches to backtest")
        return
    
    # Load ALL historical data for stats calculation
    all_historical_df = load_all_historical_data()
    
    # Build H2H cache
    print("📊 Building H2H cache...")
    h2h_cache = build_h2h_cache(all_historical_df)
    print(f"   Built {len(h2h_cache)} H2H pairs")
    
    # Initialize services
    print("🔧 Loading models...")
    ml_predictor = get_ml_predictor()
    poisson = PoissonPredictor()
    mc = MonteCarloSimulator()
    trap_detector = TrapDetector()
    gemini = get_gemini_service()
    
    # Analyze all matches
    print("🔄 Running analysis on all matches...\n")
    all_analyses = []
    
    for idx, row in matches_df.iterrows():
        try:
            home_team = row['HomeTeam']
            away_team = row['AwayTeam']
            league_id = row['League']
            match_date = row['Date']
            
            # Calculate stats from Excel data ONLY (no data leakage)
            home_stats = calculate_historical_stats(home_team, league_id,match_date, all_historical_df)
            away_stats = calculate_historical_stats(away_team, league_id, match_date, all_historical_df)
            
            # Get H2H features
            h2h_features = get_h2h_features(home_team, away_team, h2h_cache)
            
            # Extract attack/defense for Poisson/MC
            home_attack = home_stats['avg_goals_scored']
            home_defense = home_stats['avg_goals_conceded']
            away_attack = away_stats['avg_goals_scored']
            away_defense = away_stats['avg_goals_conceded']
            
            # Store actual goals for market calculations
            actual_fthg = row.get('FTHG', 0) or 0
            actual_ftag = row.get('FTAG', 0) or 0
            
            # Get odds
            odds = {
                'home': row.get('B365H', row.get('PSH', 2.0)) or 2.0,
                'draw': row.get('B365D', row.get('PSD', 3.3)) or 3.3,
                'away': row.get('B365A', row.get('PSA', 3.5)) or 3.5
            }
            
            # Calculate congestion BEFORE predictions
            home_congestion = calculate_fixture_congestion(home_team, league_id, match_date, all_historical_df)
            away_congestion = calculate_fixture_congestion(away_team, league_id, match_date, all_historical_df)
            
            # Monte Carlo prediction
            mc_result = mc.simulate(
                home_attack=home_stats.get('avg_goals_scored', 1.3),
                away_attack=away_stats.get('avg_goals_scored', 1.1),
                home_defense=home_stats.get('avg_goals_conceded', 1.1),
                away_defense=away_stats.get('avg_goals_conceded', 1.3)
            )
            
            # ML prediction with MC override
            ml_result = ml_predictor.predict(home_stats, away_stats, odds, h2h_features, congestion={
                'home_congestion_index': home_congestion.get('congestion_index', 0),
                'away_congestion_index': away_congestion.get('congestion_index', 0),
            }, mc_override={
                'prediction': mc_result.get('hdw'),
                'confidence': mc_result.get('hdw_confidence', 0)
            })
            
            # Trap detection
            trap_result = trap_detector.detect({
                'home_stats': home_stats,
                'away_stats': away_stats,
                'odds': odds,
                'h2h': h2h_features
            })
            
            # Derby detection
            derby_detector = get_derby_detector()
            row = derby_detector.detect_in_match(row)
            
            # Calculate fixture congestion for both teams
            home_congestion = calculate_fixture_congestion(home_team, league_id, match_date, all_historical_df)
            away_congestion = calculate_fixture_congestion(away_team, league_id, match_date, all_historical_df)
            
            # Add congestion info to team stats for Gemini
            home_stats['fixture_congestion'] = home_congestion
            away_stats['fixture_congestion'] = away_congestion
            
            # Poisson prediction
            poisson_result = poisson.predict(home_attack, away_attack, home_defense, away_defense)
            
            # Monte Carlo prediction
            mc_result = mc.simulate(home_attack, away_attack, home_defense, away_defense)
            
            # Trap detection
            trap_result = trap_detector.detect({
                'home_stats': home_stats,
                'away_stats': away_stats,
                'odds': odds,
                'h2h': h2h_features,
                'is_derby': row.get('is_derby', False),
                'congestion': {
                    'home_congestion_index': home_congestion.get('congestion_index', 0),
                    'away_congestion_index': away_congestion.get('congestion_index', 0),
                    'either_rotation_risk': max(home_congestion.get('likely_rotation_risk', 0), away_congestion.get('likely_rotation_risk', 0))
                }
            })
            
            # Consensus
            predictions = [
                ml_result.get('prediction', 'H'),
                poisson_result.get('prediction', 'H'),
                mc_result.get('prediction', 'H')
            ]
            
            pred_counts = {}
            for p in predictions:
                pred_counts[p] = pred_counts.get(p, 0) + 1
            
            max_agreement = max(pred_counts.values())
            
            ml_pred = ml_result.get('prediction', 'H')
            
            if max_agreement == 3:
                pattern = "STRONG_CONSENSUS"
                consensus_pred = predictions[0]
            elif max_agreement == 2:
                best_pred = max(pred_counts, key=pred_counts.get)
                # User Rule: ML Model MUST be part of the consensus
                if best_pred == ml_pred:
                    pattern = "PARTIAL_CONSENSUS"
                    consensus_pred = best_pred
                else:
                    pattern = "DIVERGENT"
                    consensus_pred = ml_pred # Default to ML if no valid consensus
            else:
                pattern = "DIVERGENT"
                consensus_pred = ml_result.get('prediction', 'H')
            
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
                'poisson_analysis': {
                    'prediction': poisson_result.get('prediction', 'H'),
                    'home_win': poisson_result.get('home_win', 0.33),
                    'draw': poisson_result.get('draw', 0.33),
                    'away_win': poisson_result.get('away_win', 0.33),
                    'over_25_probability': poisson_result.get('over_25_probability', 0.5),
                    'btts_probability': poisson_result.get('btts_probability', 0.5)
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
                'trap_detector': trap_result,
                'ml_predictions': {
                    'hdw': {
                        'prediction': ml_result.get('prediction', 'H'),
                        'confidence': ml_result.get('confidence', 0.5)
                    }
                },
                'actual_result': row.get('FTR', ''),
                'fthg': actual_fthg,
                'ftag': actual_ftag
            }
            
            all_analyses.append(analysis)
            
        except Exception as e:
            if len(all_analyses) < 3:
                print(f"⚠️ Error: {row.get('HomeTeam', 'Unknown')} - {e}")
            continue
    
    print(f"✅ Analyzed {len(all_analyses)} matches\n")
    
    # Gemini analysis
    if use_gemini and gemini.api_key and gemini.api_key != "your_gemini_api_key_here":
        print("🤖 Running Gemini AI analysis by league...")
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
        print("⏭️  Skipping Gemini analysis (no valid API key)\n")
    
    # Generate comprehensive report
    await generate_comprehensive_report(all_analyses, weeks)
    
    return all_analyses


async def generate_comprehensive_report(analyses: List[Dict], weeks: int):
    """Generate detailed markdown report with all statistics."""
    
    # Calculate all statistics
    stats = calculate_all_statistics(analyses)
    
    # Generate report
    report_path = REPORT_DIR / "backtest_report.md"
    
    with open(report_path, 'w') as f:
        f.write(f"# 10-Week Backtest Report\n\n")
        f.write(f"**Generated**: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}\n\n")
        
        # Executive Summary
        f.write("## Executive Summary\n\n")
        f.write(f"- **Period**: {stats['period_start']} to {stats['period_end']} ({weeks} weeks)\n")
        f.write(f"- **Total Matches**: {stats['total_matches']}\n")
        f.write(f"- **Overall Accuracy**: {stats['overall_accuracy']:.1f}%\n")
        f.write(f"- **Money Saved by Trap Detector**: €{stats['trap_money_saved']:,.0f}\n")
        f.write(f"- **Best Ticket Strategy ROI**: {stats['best_strategy_roi']:.1f}%\n\n")
        
        # Model Performance
        f.write("## Model Performance\n\n")
        f.write("| Model | Accuracy | Correct | Total |\n")
        f.write("|-------|----------|---------|-------|\n")
        for model, data in stats['model_performance'].items():
            f.write(f"| {model} | {data['accuracy']:.1f}% | {data['correct']}/{data['total']} |\n")
        f.write("\n")
        
        # League Performance
        f.write("## League Performance\n\n")
        f.write("| League | Matches | Accuracy | Correct | Traps Detected |\n")
        f.write("|--------|---------|----------|---------|----------------|\n")
        for league, data in sorted(stats['league_performance'].items(), key=lambda x: -x[1]['matches']):
            f.write(f"| {league} | {data['matches']} | {data['accuracy']:.1f}% | {data['correct']}/{data['matches']} | {data['traps']} |\n")
        f.write("\n")
        
        # Bet Markets Performance
        f.write("\n## Bet Markets Performance\n\n")
        f.write("| Market | Accuracy | Correct | Total | ROI |\n")
        f.write("|--------|----------|---------|-------|-----|\n")
        for market, data in stats['market_performance'].items():
            f.write(f"| {market} | {data['accuracy']:.1f}% | {data['correct']}/{data['total']} | {data['roi']:+.1f}% |\n")
        f.write("\n")
        
        # Derby Performance
        derby_perf = stats.get('derby_performance', {})
        if derby_perf.get('total_derbies', 0) > 0:
            f.write("## Derby Match Performance\n\n")
            f.write(f"**ML Model Accuracy on Derby Matches:**\n\n")
            f.write(f"- **Derby Matches**: {derby_perf.get('derby_accuracy', 0):.1f}% ({derby_perf.get('correct_derbies', 0)}/{derby_perf.get('total_derbies', 0)})\n")
            f.write(f"- **Non-Derby Matches**: {derby_perf.get('non_derby_accuracy', 0):.1f}% ({derby_perf.get('correct_non_derbies', 0)}/{derby_perf.get('total_non_derbies', 0)})\n")
            diff = derby_perf.get('derby_accuracy', 0) - derby_perf.get('non_derby_accuracy', 0)
            f.write(f"- **Difference**: {diff:+.1f}% (Derby accuracy {'higher' if diff > 0 else 'lower'} than non-derby)\n\n")
            
            # Derby breakdown by name
            if derby_perf.get('derby_names'):
                f.write("**Derby Breakdown:**\n\n")
                f.write("| Derby | Correct | Total | Accuracy |\n")
                f.write("|-------|---------|-------|----------|\n")
                for derby_name, derby_data in sorted(derby_perf['derby_names'].items(), key=lambda x: x[1]['total'], reverse=True):
                    if derby_data['total'] > 0:
                        acc = (derby_data['correct'] / derby_data['total']) * 100
                        f.write(f"| {derby_name} | {derby_data['correct']} | {derby_data['total']} | {acc:.1f}% |\n")
                f.write("\n")
        
        # Trap Detector Performance
        f.write("## Trap Detector Performance\n\n")
        f.write(f"- **Total Traps Detected**: {stats['trap_stats']['total_detected']}\n")
        f.write(f"- **Correctly Avoided**: {stats['trap_stats']['correctly_avoided']} ({stats['trap_stats']['accuracy']:.1f}% accuracy)\n")
        f.write(f"- **Money Saved**: €{stats['trap_stats']['money_saved']:,.0f}\n")
        f.write(f"- **False Positives**: {stats['trap_stats']['false_positives']}\n\n")
        f.write("**Trap Types Breakdown**:\n")
        for trap_type, count in stats['trap_stats']['types'].items():
            f.write(f"- {trap_type}: {count} cases\n")
        f.write("\n")
        
        # Ticket Strategy Results
        f.write("## Ticket Strategy Results\n\n")
        f.write("| Strategy | Tickets | Winners | Stake | Returns | Profit | ROI | Win Rate |\n")
        f.write("|----------|---------|---------|-------|---------|--------|-----|----------|\n")
        for strategy, data in stats['ticket_strategies'].items():
            f.write(f"| {strategy} | {data['tickets']} | {data['winners']} | €{data['staked']:,.0f} | €{data['returns']:,.0f} | €{data['profit']:+,.0f} | {data['roi']:+.1f}% | {data['win_rate']:.1f}% |\n")
        f.write("\n")
        
        # Recommendations
        f.write("## Recommendations\n\n")
        f.write(generate_recommendations(stats))
    
    print(f"📄 Report generated: {report_path}")
    print(f"\n{'=' * 70}")
    print("✅ Backtest Complete!")
    print(f"{'=' * 70}\n")


def calculate_all_statistics(analyses: List[Dict]) -> Dict:
    """Calculate all statistics for the report."""
    
    if not analyses:
        return {}
    
    stats = {
        'total_matches': len(analyses),
        'period_start': min(a['date'] for a in analyses if a.get('date')),
        'period_end': max(a['date'] for a in analyses if a.get('date')),
    }
    
    # Model performance
    model_stats = {
        'ML Model': {'correct': 0, 'total': 0},
        'Poisson': {'correct': 0, 'total': 0},
        'Monte Carlo': {'correct': 0, 'total': 0},
        'Gemini AI': {'correct': 0, 'total': 0},
        'Consensus': {'correct': 0, 'total': 0}
    }
    
    league_stats = {}
    trap_stats = {
        'total_detected': 0,
        'correctly_avoided': 0,
        'false_positives': 0,
        'money_saved': 0,
        'types': {}
    }
    
    market_stats = {
        'Home/Draw/Away': {'correct': 0, 'total': 0, 'profit': 0},
        'Over/Under 2.5': {'correct': 0, 'total': 0, 'profit': 0},
        'BTTS': {'correct': 0, 'total': 0, 'profit': 0}
    }
    
    for match in analyses:
        actual = match.get('actual_result', '')
        if not actual:
            continue
        
        # Model performance
        ml_pred = match.get('ml_analysis', {}).get('prediction')
        poisson_pred = match.get('poisson_analysis', {}).get('prediction')
        mc_pred = match.get('monte_carlo_analysis', {}).get('prediction')
        
        # Get Gemini prediction - check both possible locations
        gemini_analysis = match.get('gemini_analysis', {})
        if isinstance(gemini_analysis, dict) and gemini_analysis:
            gemini_pred = gemini_analysis.get('prediction')
        else:
            gemini_pred = None
            
        consensus_pred = match.get('pattern_analysis', {}).get('consensus_prediction')
        
        model_stats['ML Model']['total'] += 1
        model_stats['Poisson']['total'] += 1
        model_stats['Monte Carlo']['total'] += 1
        model_stats['Consensus']['total'] += 1
        
        if ml_pred == actual:
            model_stats['ML Model']['correct'] += 1
        if poisson_pred == actual:
            model_stats['Poisson']['correct'] += 1
        if mc_pred == actual:
            model_stats['Monte Carlo']['correct'] += 1
        if consensus_pred == actual:
            model_stats['Consensus']['correct'] += 1
        
        # Count Gemini if prediction exists and is not from fallback
        if gemini_pred and gemini_analysis.get('confidence', 0) > 0:
            model_stats['Gemini AI']['total'] += 1
            if gemini_pred == actual:
                model_stats['Gemini AI']['correct'] += 1
        
        # League performance
        league = match.get('league', 'Unknown')
        if league not in league_stats:
            league_stats[league] = {'matches': 0, 'correct': 0, 'traps': 0}
        league_stats[league]['matches'] += 1
        if consensus_pred == actual:
            league_stats[league]['correct'] += 1
        
        # Trap detection
        trap_result = match.get('trap_detector', {})
        if trap_result.get('is_trap'):
            trap_stats['total_detected'] += 1
            league_stats[league]['traps'] += 1
            
            for flag in trap_result.get('flags', []):
                trap_stats['types'][flag] = trap_stats['types'].get(flag, 0) + 1
            
            # If trap was flagged and prediction was wrong, we saved money
            if consensus_pred != actual:
                trap_stats['correctly_avoided'] += 1
                trap_stats['money_saved'] += 100
            else:
                trap_stats['false_positives'] += 1
        
        # Market performance
        market_stats['Home/Draw/Away']['total'] += 1
        if consensus_pred == actual:
            market_stats['Home/Draw/Away']['correct'] += 1
            odds = match.get('odds', {})
            odds_key = 'home' if actual == 'H' else ('away' if actual == 'A' else 'draw')
            market_stats['Home/Draw/Away']['profit'] += (odds.get(odds_key, 2.0) - 1) * 100
        else:
            market_stats['Home/Draw/Away']['profit'] -= 100
        
        # Over/Under 2.5 (using actual goals from match)
        poisson = match.get('poisson_analysis', {})
        over_25_prob = poisson.get('over_25_probability', 0.5)
        over_pred = "Over" if over_25_prob > 0.5 else "Under"
        
        # Get actual goals from the match
        actual_fthg = match.get('fthg', 0)
        actual_ftag = match.get('ftag', 0)
        actual_total = actual_fthg + actual_ftag
        actual_over_25 = "Over" if actual_total > 2.5 else "Under"
        
        market_stats['Over/Under 2.5']['total'] += 1
        if over_pred == actual_over_25:
            market_stats['Over/Under 2.5']['correct'] += 1
            market_stats['Over/Under 2.5']['profit'] += (1.9 - 1) * 100  # Typical odds ~1.9
        else:
            market_stats['Over/Under 2.5']['profit'] -= 100
        
        # BTTS (Both Teams To Score)
        btts_prob = poisson.get('btts_probability', 0.5)
        btts_pred = "Yes" if btts_prob > 0.5 else "No"
        actual_btts = "Yes" if (actual_fthg > 0 and actual_ftag > 0) else "No"
        
        market_stats['BTTS']['total'] += 1
        if btts_pred == actual_btts:
            market_stats['BTTS']['correct'] += 1
            market_stats['BTTS']['profit'] += (1.8 - 1) * 100  # Typical odds ~1.8
        else:
            market_stats['BTTS']['profit'] -= 100
    
    # Calculate accuracies and ROI
    for model, data in model_stats.items():
        if data['total'] > 0:
            data['accuracy'] = (data['correct'] / data['total']) * 100
        else:
            data['accuracy'] = 0
    
    for league, data in league_stats.items():
        data['accuracy'] = (data['correct'] / data['matches']) * 100 if data['matches'] > 0 else 0
    
    for market, data in market_stats.items():
        if data['total'] > 0:
            data['accuracy'] = (data['correct'] / data['total']) * 100
            data['roi'] = (data['profit'] / (data['total'] * 100)) * 100
        else:
            data['accuracy'] = 0
            data['roi'] = 0
    
    # Trap detection accuracy
    total_trap_predictions = trap_stats['correctly_avoided'] + trap_stats['false_positives']
    trap_stats['accuracy'] = (trap_stats['correctly_avoided'] / total_trap_predictions * 100) if total_trap_predictions > 0 else 0
    
    # Derby performance analysis
    from app.core.derby_detector import get_derby_detector
    derby_detector = get_derby_detector()
    derby_performance = derby_detector.analyze_derby_performance(analyses)
    
    # Ticket strategies
    ticket_strategies = calculate_ticket_strategies(analyses)
    
    # Overall stats
    stats['overall_accuracy'] = model_stats['Consensus']['accuracy']
    stats['trap_money_saved'] = trap_stats['money_saved']
    stats['best_strategy_roi'] = max([s['roi'] for s in ticket_strategies.values()]) if ticket_strategies else 0
    
    stats['model_performance'] = model_stats
    stats['league_performance'] = league_stats
    stats['market_performance'] = market_stats
    stats['trap_stats'] = trap_stats
    stats['ticket_strategies'] = ticket_strategies
    
    return stats


def calculate_ticket_strategies(analyses: List[Dict]) -> Dict:
    """Calculate different ticket betting strategies with LOWER LEAGUE FILTERS"""
    
    strategies = {
        'High Confidence (70%+, No Traps)': [],
        'Strong Consensus (All Agree, No Traps)': [],
        'Conservative (65%+, 2/3 Agree, No Traps)': [],
        'Over 2.5 Goals (60%+ Probability)': [],
        'BTTS Yes (60%+ Probability)': [],
        'Combined: HDW + Over 2.5 (60%+)': []
    }
    
    MIN_ODDS = 1.77  # Minimum odds for ticket inclusion (updated from 1.70)
    
    # Lower league specific filters
    LOWER_LEAGUES = ['Championship', 'League One', 'League Two']
    
    def should_include_lower_league_bet(match, predicted_odds):
        """Filter to avoid 83% of lower league failures"""
        league = match.get('league', '')
        
        if league in LOWER_LEAGUES:
            # SKIP moderate favorites (1.5-2.5 odds) - danger zone
            if 1.5 <= predicted_odds <= 2.5:
                return False
            
            # Check draw risk for strong favorites
            if predicted_odds < 1.5:
                h2h = match.get('h2h_features', {})
                draw_rate = h2h.get('draw_rate', 0)
                if draw_rate > 0.35:  # 35%+ historical draws
                    return False
        
        return True
    
    for match in analyses:
        ml_conf = match.get('ml_analysis', {}).get('confidence', 0)
        pattern = match.get('pattern_analysis', {}).get('pattern', '')
        ml_pred = match.get('ml_analysis', {}).get('prediction')
        consensus_pred = match.get('pattern_analysis', {}).get('consensus_prediction')
        is_trap = match.get('trap_detector', {}).get('is_trap', False)
        actual = match.get('actual_result', '')
        
        # Get predicted odds for the bet
        odds = match.get('odds', {})
        if ml_pred == 'H':
            pred_odds = odds.get('home', 99)
        elif ml_pred == 'A':
            pred_odds = odds.get('away', 99)
        else:
            pred_odds = odds.get('draw', 99)
        
        # Apply lower league filter FIRST
        if not should_include_lower_league_bet(match, pred_odds):
            continue  # Skip this match - it's in the danger zone
        
        # Check minimum odds
        if pred_odds < MIN_ODDS:
            continue
        
        if not actual:
            continue
        
        # Strategy 1: High Confidence
        if ml_conf >= 0.70 and not is_trap:
            strategies['High Confidence (70%+, No Traps)'].append(match)
        
        # Strategy 2: Strong Consensus
        if pattern == 'STRONG_CONSENSUS' and ml_conf >= 0.65 and not is_trap:
            strategies['Strong Consensus (All Agree, No Traps)'].append(match)
        
        # Strategy 3: Conservative
        if ml_conf >= 0.65 and pattern in ['STRONG_CONSENSUS', 'PARTIAL_CONSENSUS'] and not is_trap:
            strategies['Conservative (65%+, 2/3 Agree, No Traps)'].append(match)
        
        # Strategy 4: Over 2.5 Goals
        over_25_prob = match.get('poisson_analysis', {}).get('over_25_probability', 0)
        if over_25_prob >= 0.60:
            strategies['Over 2.5 Goals (60%+ Probability)'].append(match)
        
        # Strategy 5: BTTS Yes
        btts_prob = match.get('poisson_analysis', {}).get('btts_probability', 0)
        if btts_prob >= 0.60:
            strategies['BTTS Yes (60%+ Probability)'].append(match)
        
        # Strategy 6: Combined (HDW + Over 2.5)
        if ml_conf >= 0.60 and over_25_prob >= 0.60 and not is_trap:
            strategies['Combined: HDW + Over 2.5 (60%+)'].append(match)
    
    # Calculate ROI for each strategy
    results = {}
    
    for strategy_name, matches in strategies.items():
        if len(matches) < 3:
            results[strategy_name] = {
                'tickets': 0, 'winners': 0, 'staked': 0, 
                'returns': 0, 'profit': 0, 'roi': 0, 'win_rate': 0,
                'weekly_breakdown': {}
            }
            continue
        
        # Generate tickets (3 games each)
        num_tickets = len(matches) // 3
        winners = 0
        total_returns = 0
        
        for i in range(num_tickets):
            ticket_matches = matches[i*3:(i+1)*3]
            combined_odds = 1.0
            all_correct = True
            
            for m in ticket_matches:
                # Determine prediction type based on strategy
                if 'Over 2.5' in strategy_name:
                    over_25_prob = m.get('poisson_analysis', {}).get('over_25_probability', 0.5)
                    pred = "Over" if over_25_prob > 0.5 else "Under"
                    actual_total = m.get('fthg', 0) + m.get('ftag', 0)
                    actual = "Over" if actual_total > 2.5 else "Under"
                    odds_val = 1.9
                elif 'BTTS' in strategy_name:
                    btts_prob = m.get('poisson_analysis', {}).get('btts_probability', 0.5)
                    pred = "Yes" if btts_prob > 0.5 else "No"
                    actual = "Yes" if (m.get('fthg', 0) > 0 and m.get('ftag', 0) > 0) else "No"
                    odds_val = 1.8
                elif 'Combined' in strategy_name:
                    # Combined strategies need both predictions correct
                    pred_hdw = m.get('pattern_analysis', {}).get('consensus_prediction')
                    actual_hdw = m.get('actual_result')
                    over_25_prob = m.get('poisson_analysis', {}).get('over_25_probability', 0.5)
                    pred_over = "Over" if over_25_prob > 0.5 else "Under"
                    actual_total = m.get('fthg', 0) + m.get('ftag', 0)
                    actual_over = "Over" if actual_total > 2.5 else "Under"
                    
                    if pred_hdw != actual_hdw or pred_over != actual_over:
                        all_correct = False
                    
                    odds = m.get('odds', {})
                    odds_key = 'home' if pred_hdw == 'H' else ('away' if pred_hdw == 'A' else 'draw')
                    odds_val = odds.get(odds_key, 2.0) * 1.9  # HDW odds * Over 2.5 odds
                    combined_odds *= odds_val
                    continue
                else:
                    # HDW strategies
                    pred = m.get('pattern_analysis', {}).get('consensus_prediction')
                    actual = m.get('actual_result')
                    odds = m.get('odds', {})
                    odds_key = 'home' if pred == 'H' else ('away' if pred == 'A' else 'draw')
                    odds_val = odds.get(odds_key, 2.0)
                
                if pred != actual:
                    all_correct = False
                
                combined_odds *= odds_val
            
            if all_correct:
                winners += 1
                total_returns += 100 * combined_odds
        
        staked = num_tickets * 100
        profit = total_returns - staked
        roi = (profit / staked * 100) if staked > 0 else 0
        win_rate = (winners / num_tickets * 100) if num_tickets > 0 else 0
        
        results[strategy_name] = {
            'tickets': num_tickets,
            'winners': winners,
            'staked': staked,
            'returns': total_returns,
            'profit': profit,
            'roi': roi,
            'win_rate': win_rate
        }
    
    return results


def generate_recommendations(stats: Dict) -> str:
    """Generate AI recommendations based on statistics."""
    
    recommendations = []
    
    # Best model
    best_model = max(stats['model_performance'].items(), key=lambda x: x[1]['accuracy'])
    recommendations.append(f"**Best Performing Model**: {best_model[0]} with {best_model[1]['accuracy']:.1f}% accuracy")
    
    # Best league
    best_league = max(stats['league_performance'].items(), key=lambda x: x[1]['accuracy'])
    recommendations.append(f"**Best League**: {best_league[0]} with {best_league[1]['accuracy']:.1f}% accuracy")
    
    # Trap detection value
    if stats['trap_stats']['total_detected'] > 0:
        recommendations.append(f"**Trap Detector Value**: Saved €{stats['trap_stats']['money_saved']:,.0f} by avoiding {stats['trap_stats']['correctly_avoided']} losing bets")
    
    # Best ticket strategy
    best_strategy = max(stats['ticket_strategies'].items(), key=lambda x: x[1]['roi'])
    recommendations.append(f"**Best Ticket Strategy**: {best_strategy[0]} with {best_strategy[1]['roi']:+.1f}% ROI")
    
    return "\n".join(f"{i+1}. {rec}" for i, rec in enumerate(recommendations))


def main():
    import argparse
    parser = argparse.ArgumentParser(description="Run enhanced 10-week backtest")
    parser.add_argument("--weeks", type=int, default=10, help="Number of weeks to backtest")
    parser.add_argument("--no-gemini", action="store_true", help="Skip Gemini AI analysis")
    args = parser.parse_args()
    
    asyncio.run(run_enhanced_backtest(args.weeks, use_gemini=not args.no_gemini))


if __name__ == "__main__":
    main()
