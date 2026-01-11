"""
Backtest API Route - REAL DATA from Historical Excel
"""
from fastapi import APIRouter, Query
from typing import Optional, List
import pandas as pd
from pathlib import Path
from datetime import datetime, timedelta

from app.core.poisson import PoissonPredictor
from app.core.monte_carlo import MonteCarloSimulator
from app.services.team_stats import get_team_stats_service

router = APIRouter()

DATA_DIR = Path(__file__).parent.parent.parent.parent / "data"

# League mapping
LEAGUE_NAMES = {
    'E0': 'Premier League', 'E1': 'Championship', 'E2': 'League One', 'E3': 'League Two',
    'D1': 'Bundesliga', 'D2': '2. Bundesliga',
    'I1': 'Serie A', 'I2': 'Serie B',
    'F1': 'Ligue 1', 'F2': 'Ligue 2',
    'SP1': 'La Liga'
}


def load_historical_matches(weeks: int, league_filter: Optional[str] = None):
    """Load historical matches for backtesting"""
    # Load valid leagues
    import json
    leagues_file = DATA_DIR / "leagues.json"
    valid_leagues = []
    if leagues_file.exists():
        with open(leagues_file, 'r') as f:
            leagues = json.load(f)
            valid_leagues = [l['id'] for l in leagues]
    
    all_matches = []
    historical_dir = DATA_DIR / "historical"
    
    # Load recent Excel files
    excel_files = sorted(historical_dir.glob("*.xlsx"), reverse=True)[:2]  # Last 2 seasons for backtest
    
    for excel_file in excel_files:
        try:
            xl = pd.ExcelFile(excel_file)
            for sheet_name in xl.sheet_names:
                if sheet_name not in valid_leagues:
                    continue
                if league_filter and sheet_name != league_filter:
                    continue
                
                df = pd.read_excel(excel_file, sheet_name=sheet_name)
                if 'Date' in df.columns and 'FTR' in df.columns:
                    df['Date'] = pd.to_datetime(df['Date'], errors='coerce')
                    df['League'] = sheet_name
                    all_matches.append(df)
        except Exception as e:
            print(f"Error loading {excel_file}: {e}")
    
    if not all_matches:
        return pd.DataFrame()
    
    combined = pd.concat(all_matches, ignore_index=True)
    
    # Filter to last N weeks
    cutoff_date = datetime.now() - timedelta(weeks=weeks)
    combined = combined[combined['Date'] >= cutoff_date]
    
    return combined.sort_values('Date', ascending=False)


def run_backtest_analysis(matches_df: pd.DataFrame):
    """Run backtest analysis on historical matches"""
    poisson = PoissonPredictor()
    monte_carlo = MonteCarloSimulator()
    stats_service = get_team_stats_service()
    
    results = []
    correct_count = 0
    total_count = 0
    
    pattern_stats = {
        'strong_consensus': {'correct': 0, 'total': 0},
        'partial_consensus': {'correct': 0, 'total': 0}
    }
    
    league_stats = {}
    
    for _, row in matches_df.iterrows():
        try:
            home_team = row['HomeTeam']
            away_team = row['AwayTeam']
            league_id = row['League']
            match_date = row['Date']
            actual_result = row['FTR']  # H, D, A
            
            # Get team stats BEFORE match date
            home_stats = stats_service.get_team_stats(home_team, league_id, before_date=match_date)
            away_stats = stats_service.get_team_stats(away_team, league_id, before_date=match_date)
            
            # Make prediction
            home_attack = home_stats['avg_goals_scored'] if home_stats['avg_goals_scored'] > 0 else 1.3
            away_attack = away_stats['avg_goals_scored'] if away_stats['avg_goals_scored'] > 0 else 1.2
            home_defense = home_stats['avg_goals_conceded'] if home_stats['avg_goals_conceded'] > 0 else 1.0
            away_defense = away_stats['avg_goals_conceded'] if away_stats['avg_goals_conceded'] > 0 else 1.1
            
            poisson_result = poisson.predict(home_attack, away_attack, home_defense, away_defense)
            mc_result = monte_carlo.simulate(home_attack, away_attack, home_defense, away_defense)
            
            prediction = poisson_result['hdw']
            confidence = poisson_result['hdw_confidence']
            
            # Pattern analysis
            all_agree = poisson_result['hdw'] == mc_result['hdw']
            pattern = "STRONG_CONSENSUS" if all_agree else "PARTIAL_CONSENSUS"
            
            # Check if correct
            is_correct = prediction == actual_result
            
            # Get odds
            home_odds = float(row.get('B365H', 2.0)) if pd.notna(row.get('B365H')) else 2.0
            
            results.append({
                "date": match_date.strftime('%Y-%m-%d'),
                "match": f"{home_team} vs {away_team}",
                "league": LEAGUE_NAMES.get(league_id, league_id),
                "league_id": league_id,
                "prediction": prediction,
                "actual": actual_result,
                "correct": is_correct,
                "confidence": round(confidence, 2),
                "pattern": pattern,
                "odds": round(home_odds, 2),
                "message": f"✅ Correct - {'Home Win' if actual_result == 'H' else 'Draw' if actual_result == 'D' else 'Away Win'}" if is_correct else f"❌ Incorrect - Predicted {prediction}, Actual {actual_result}"
            })
            
            total_count += 1
            if is_correct:
                correct_count += 1
            
            # Pattern stats
            pattern_key = 'strong_consensus' if pattern == 'STRONG_CONSENSUS' else 'partial_consensus'
            pattern_stats[pattern_key]['total'] += 1
            if is_correct:
                pattern_stats[pattern_key]['correct'] += 1
            
            # League stats
            if league_id not in league_stats:
                league_stats[league_id] = {'correct': 0, 'total': 0, 'profit': 0}
            league_stats[league_id]['total'] += 1
            if is_correct:
                league_stats[league_id]['correct'] += 1
                league_stats[league_id]['profit'] += (home_odds - 1)
            else:
                league_stats[league_id]['profit'] -= 1
                
        except Exception as e:
            continue
    
    return results, pattern_stats, league_stats, correct_count, total_count


@router.get("/backtest")
async def get_backtest(
    weeks: int = Query(15, ge=1, le=52),
    league: Optional[str] = Query(None, description="Filter by league ID"),
    offset: int = Query(0, ge=0),
    limit: int = Query(50, ge=1, le=200)
):
    """Get comprehensive backtesting results from REAL historical data"""
    
    # Run blocking code in thread pool to avoid blocking event loop
    import asyncio
    loop = asyncio.get_event_loop()
    
    # Load historical matches
    matches_df = await loop.run_in_executor(None, load_historical_matches, weeks, league)
    
    if matches_df.empty:
        return {
            "summary": {"total_matches": 0, "message": "No historical data found"},
            "games_analysis": {"offset": 0, "limit": limit, "total": 0, "items": []},
            "league_performance": [],
            "roi_calculation": {}
        }
    
    # Run backtest in executor to avoid blocking event loop
    results, pattern_stats, league_stats, correct_count, total_count = await loop.run_in_executor(
        None,
        run_backtest_analysis,
        matches_df
    )
    
    # Calculate accuracies
    overall_accuracy = correct_count / total_count if total_count > 0 else 0
    
    strong_acc = pattern_stats['strong_consensus']['correct'] / pattern_stats['strong_consensus']['total'] if pattern_stats['strong_consensus']['total'] > 0 else 0
    partial_acc = pattern_stats['partial_consensus']['correct'] / pattern_stats['partial_consensus']['total'] if pattern_stats['partial_consensus']['total'] > 0 else 0
    
    # Summary
    summary = {
        "period": f"{matches_df['Date'].min().strftime('%Y-%m-%d')} to {matches_df['Date'].max().strftime('%Y-%m-%d')}",
        "total_matches": total_count,
        "correct_predictions": correct_count,
        "overall_accuracy": round(overall_accuracy, 3),
        "pattern_breakdown": {
            "strong_consensus": {
                "matches": pattern_stats['strong_consensus']['total'],
                "accuracy": round(strong_acc, 3)
            },
            "partial_consensus": {
                "matches": pattern_stats['partial_consensus']['total'],
                "accuracy": round(partial_acc, 3)
            }
        }
    }
    
    # League performance
    league_performance = []
    for league_id, stats in league_stats.items():
        accuracy = stats['correct'] / stats['total'] if stats['total'] > 0 else 0
        roi = (stats['profit'] / stats['total']) * 100 if stats['total'] > 0 else 0
        league_performance.append({
            "league_id": league_id,
            "league_name": LEAGUE_NAMES.get(league_id, league_id),
            "matches": stats['total'],
            "accuracy": round(accuracy, 3),
            "roi": round(roi, 1)
        })
    
    league_performance.sort(key=lambda x: x['accuracy'], reverse=True)
    
    # ROI calculation (3 games/ticket, €100/ticket)
    # Simulate tickets from high-confidence predictions
    high_conf_results = [r for r in results if r['pattern'] == 'STRONG_CONSENSUS']
    tickets_possible = len(high_conf_results) // 3
    winning_tickets = 0
    
    for i in range(tickets_possible):
        ticket_games = high_conf_results[i*3:(i+1)*3]
        if all(g['correct'] for g in ticket_games):
            winning_tickets += 1
    
    total_staked = tickets_possible * 100
    avg_combined_odds = 3.5  # Approximate
    total_returns = winning_tickets * 100 * avg_combined_odds
    
    roi_calculation = {
        "rules": {
            "games_per_ticket": 3,
            "stake_per_ticket": 100,
            "max_tickets_per_fixture": 4
        },
        "results": {
            "total_tickets": tickets_possible,
            "winning_tickets": winning_tickets,
            "losing_tickets": tickets_possible - winning_tickets,
            "total_staked": total_staked,
            "total_returns": round(total_returns, 2),
            "profit": round(total_returns - total_staked, 2),
            "roi_percentage": round(((total_returns - total_staked) / total_staked) * 100, 2) if total_staked > 0 else 0,
            "win_rate": round(winning_tickets / tickets_possible, 3) if tickets_possible > 0 else 0
        }
    }
    
    # Paginate results
    paginated_results = results[offset:offset + limit]
    
    return {
        "summary": summary,
        "games_analysis": {
            "offset": offset,
            "limit": limit,
            "total": len(results),
            "items": paginated_results
        },
        "league_performance": league_performance,
        "roi_calculation": roi_calculation
    }
