from fastapi import APIRouter, Depends, Query, HTTPException
from typing import List, Optional, Dict, Any
from datetime import datetime, date, timedelta

from src.api.dependencies import ServiceContainer, get_time_travel_backtest_service
from src.domain.services.time_travel_backtest_service import TimeTravelBacktestService
from src.utils.logger import get_logger

router = APIRouter()
logger = get_logger("BacktestRouter")

@router.post("/run_time_travel")
async def run_time_travel_backtest(
    weeks: int = Query(4, description="Number of past weeks to backtest"),
    service: TimeTravelBacktestService = Depends(get_time_travel_backtest_service)
):
    """
    Run a rigorous time-travel backtest.
    
    1. Scans 'upcoming' directory for past fixtures.
    2. Runs analysis for each match using ONLY history available at that time.
    3. Verifies predictions against actual results in Historical DB.
    4. Returns detailed Analytics (Accuracy, ROI, Charts).
    """
    logger.info(f"Starting Time Travel Backtest for last {weeks} weeks")
    
    try:
        # 1. Gather Matches
        # We need a way to get "Past Upcoming" matches.
        # Option A: Parse them from saved 'upcoming/*.csv' files if we kept them?
        # Option B: Use the Historical DB itself as the source of "fixtures".
        # 
        # Using Historical DB is cleaner because we know the result exists.
        # We just need to pretend we don't know the result during analysis.
        
        # Sourcing from Historical DB:
        all_matches = service.historical_repo.get_all()
        
        # Filter for last N weeks
        today = datetime.now().date()
        cutoff = today - datetime.timedelta(weeks=weeks)
        
        target_matches = [
            m.__dict__ for m in all_matches 
            if m.match_date >= cutoff and m.match_date < today
        ]
        
        # Enhance dictionary for compatibility (Match object -> Dict expected by Service)
        # The service expects keys like 'Date', 'HomeTeam' to parse. 
        # Match object has snake_case.
        # Let's handle this adapter in the service or here.
        # Service uses `match.get("Date")`. 
        # Let's convert Match entities to the dict format expected.
        
        adapted_matches = []
        for m in target_matches:
            adapted_matches.append({
                "Date": m.match_date,
                "HomeTeam": m.home_team,
                "AwayTeam": m.away_team,
                "Div": m.league,
                "FTR": m.ftr,
                "FTHG": m.fthg,
                "FTAG": m.ftag
                # Add odds if available in Match entity?
            })
            
        logger.info(f"Found {len(adapted_matches)} matches to backtest.")
        
        # 2. Run Backtest
        results = await service.run_backtest(adapted_matches)
        
        return results
        
    except Exception as e:
        logger.error(f"Time Travel Backtest failed: {e}")
        raise HTTPException(status_code=500, detail=str(e))
