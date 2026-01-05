from datetime import datetime, date, timedelta
from typing import List, Optional, Dict, Any
import pandas as pd
from fastapi import APIRouter, Depends, HTTPException, Query
from pydantic import BaseModel

from src.utils.logger import get_logger
from src.api.dependencies import (
    get_prediction_service,
    get_fixture_service,
    get_dixon_coles,
    get_match_stats_service,
    get_h2h_service,
    get_historical_matches
)
from src.domain.services.prediction_service import PredictionService
from src.domain.services.fixture_service import FixtureService
from src.statistics.dixon_coles_model import DixonColesModel
from src.domain.services.match_stats_service import MatchStatsService
from src.domain.services.h2h_service import H2HService
from src.utils.stats_utils import round_to_precision

router = APIRouter()
logger = get_logger("BacktestRouter")

class BacktestRequest(BaseModel):
    start_date: Optional[str] = None
    end_date: Optional[str] = None
    leagues: Optional[List[str]] = None
    initial_bankroll: float = 1000.0
    stake_size: float = 10.0  # Fixed stake or %? Let's assume flat for now

class BacktestResponse(BaseModel):
    total_bets: int
    wins: int
    losses: int
    win_rate: float
    roi: float
    profit: float
    bankroll_history: List[float]
    predictions: List[Dict[str, Any]]
    generated_at: str

@router.post("/run", response_model=BacktestResponse)
async def run_backtest(
    request: BacktestRequest,
    prediction_service: PredictionService = Depends(get_prediction_service),
    fixture_service: FixtureService = Depends(get_fixture_service), # Used to load CSV
    dixon_coles: DixonColesModel = Depends(get_dixon_coles),
    match_stats_service: MatchStatsService = Depends(get_match_stats_service),
    h2h_service: H2HService = Depends(get_h2h_service),
    historical_matches: list = Depends(get_historical_matches),
):
    """
    Run a backtest simulation on historical data.
    
    WARNING: This is computationally expensive.
    """
    logger.info(f"Starting backtest from {request.start_date} to {request.end_date}")
    
    # 1. Load Data
    # For backtesting, we need "fixtures" that ALREADY HAPPENED and have results.
    # We can use the main historical dataset.
    # fixture_service.load_upcoming_fixtures loads only future (or recent w/o results).
    # We should filter `historical_matches` for the date range.
    
    target_matches = []
    
    # Convert dates
    start_dt = datetime.strptime(request.start_date, "%Y-%m-%d").date() if request.start_date else date.today() - timedelta(days=30)
    end_dt = datetime.strptime(request.end_date, "%Y-%m-%d").date() if request.end_date else date.today()
    
    # Filter historical matches
    for m in historical_matches:
        m_date = m.get("match_date")
        if isinstance(m_date, str):
            try:
                m_date = datetime.fromisoformat(m_date[:10]).date()
            except:
                continue
        elif isinstance(m_date, datetime):
            m_date = m_date.date()
            
        if not m_date:
            continue
            
        if start_dt <= m_date <= end_dt:
            # Check league filter
            if request.leagues and m.get("league") not in request.leagues:
                continue
                
            # Must have Result (FTR) to be valid for backtest
            if not m.get("ftr"):
                continue
                
            target_matches.append(m)
            
    logger.info(f"Found {len(target_matches)} matches for backtest period.")
    
    if not target_matches:
        return BacktestResponse(
            total_bets=0, wins=0, losses=0, win_rate=0.0, roi=0.0, profit=0.0,
            bankroll_history=[], predictions=[], generated_at=datetime.now().isoformat()
        )

    # 2. Run Predictions (Time Travel)
    # Sort by date so bankroll simulation is chronological
    target_matches.sort(key=lambda x: str(x.get("match_date")))
    
    results = []
    current_bankroll = request.initial_bankroll
    bankroll_history = [current_bankroll]
    wins = 0
    losses = 0
    
    # Initialize services if needed (DixonColes)
    # Using passed instances
    
    for i, match in enumerate(target_matches):
        # logging progress
        if i % 10 == 0:
            logger.info(f"Backtesting match {i+1}/{len(target_matches)}")
            
        # Run Analysis - effectively "predict" this match
        # IMPORTANT: modify `analyze_matches_for_date` logic or call `predict_match` directly?
        # `analyze_matches_for_date` is better as it includes all logic (Ensemble etc)
        # BUT it expects a list. We can pass list of 1.
        
        # We need to construct a "fixture" like object from the historical match
        # but WITHOUT the result info visible to the predictor (though predictor services use `historical_matches` excluding current)
        
        # Actually, `analyze_matches_for_date` takes `matches` and `historical_matches`.
        # The key is that `predict_match` inside it relies on `FeatureEngineeringService`
        # which now effectively uses `as_of_date = match_date - 1`.
        
        # However, `analyze_matches_for_date` does NOT typically take a date override for `historical_matches`
        # It relies on `historical_matches` being the full dataset.
        # OUR LEAKAGE FIX IS INSIDE THE SERVICES (H2H, Referee, Standings).
        # They now check `as_of_date` vs `match_date`.
        # So we can pass the full `historical_matches` list.
        # Be careful: `predict_match` needs to know THIS match's date to pass it as `as_of_date`.
        
        analyzed_list = prediction_service.analyze_matches_for_date(
            matches=[match],
            historical_matches=historical_matches, # Passed full history, safer services filter it
            dixon_coles_model=dixon_coles,
            match_stats_service=match_stats_service,
            h2h_service=h2h_service
            # No exclude_fallback needed
        )
        
        if not analyzed_list:
            continue
            
        prediction = analyzed_list[0]
        
        # 3. Simulate Betting
        # Strategy: Bet on highest confidence outcome if above threshold
        # Simple strategy: Win/Draw if conf > 60%, Over2.5/BTTS if conf > 55%
        
        # Extract Actual Result
        actual_ftr = match.get("ftr")
        actual_goals = (match.get("fthg") or 0) + (match.get("ftag") or 0)
        actual_btts = (match.get("fthg") or 0) > 0 and (match.get("ftag") or 0) > 0
        
        # Determine Bet
        # Custom logic for "Best Bet"
        bet_placed = None
        bet_result = None # "win", "loss"
        odds_taken = 0.0
        
        # Check Winner Prediction
        # New Structure: analysis -> result -> prediction/probability
        result_analysis = prediction.get("analysis", {}).get("result", {})
        res_pick = result_analysis.get("prediction", "D")
        res_conf = float(result_analysis.get("probability", 0.0))
        
        # Check Goal Prediction
        # New Structure: analysis -> over25 -> probability
        goals_analysis = prediction.get("analysis", {}).get("over25", {})
        goals_conf = float(goals_analysis.get("probability", 0.0))
        
        # Simple Selection Logic (matches Weekly Ticket rules roughly)
        if res_conf >= 0.55 and res_pick in ['H', 'A']:
            bet_type = "Winner"
            selection = res_pick
            odds = match.get(f"b365{res_pick.lower()}") or 2.0
            
            # Check Outcome
            if actual_ftr == selection:
                bet_result = "win"
                profit = request.stake_size * (float(odds) - 1)
                current_bankroll += profit
                odds_taken = float(odds)
            else:
                bet_result = "loss"
                current_bankroll -= request.stake_size
                odds_taken = float(odds)
                
            bet_placed = f"{selection} Win"
            
        elif goals_conf >= 0.55:
             # Over 2.5
             bet_type = "Over 2.5"
             odds = match.get("b365_over25") or match.get("over25_odds") or 1.8
             
             if actual_goals > 2.5:
                 bet_result = "win"
                 profit = request.stake_size * (float(odds) - 1)
                 current_bankroll += profit
                 odds_taken = float(odds)
             else:
                 bet_result = "loss"
                 current_bankroll -= request.stake_size
                 odds_taken = float(odds)
                 
             bet_placed = "Over 2.5"
             
        # Record Result
        if bet_placed:
            if bet_result == "win":
                wins += 1
            else:
                losses += 1
                
            bankroll_history.append(round_to_precision(current_bankroll))
            
            results.append({
                "date": str(match.get("match_date"))[:10],
                "match": f"{match.get('home_team')} vs {match.get('away_team')}",
                "bet": bet_placed,
                "odds": odds_taken,
                "confidence": max(res_conf, goals_conf),
                "result": bet_result,
                "profit": profit if bet_result == "win" else -request.stake_size,
                "actual_score": f"{match.get('fthg')}-{match.get('ftag')}"
            })

    total_bets = wins + losses
    starting_bankroll = request.initial_bankroll
    profit = current_bankroll - starting_bankroll
    roi = (profit / (total_bets * request.stake_size)) * 100 if total_bets > 0 else 0.0
    win_rate = (wins / total_bets) * 100 if total_bets > 0 else 0.0
    
    return BacktestResponse(
        total_bets=total_bets,
        wins=wins,
        losses=losses,
        win_rate=round_to_precision(win_rate),
        roi=round_to_precision(roi),
        profit=round_to_precision(profit),
        bankroll_history=bankroll_history,
        predictions=results,
        generated_at=datetime.now().isoformat()
    )
