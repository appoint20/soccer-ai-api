from datetime import datetime, date, timedelta
from typing import List, Optional, Dict, Any
import pandas as pd
from fastapi import APIRouter, Depends, HTTPException, Query
from pydantic import BaseModel

from src.utils.logger import get_logger
from src.api.dependencies import (
    get_match_analyzer,
    get_historical_matches
)
from src.application.use_cases.analyze_matches import MatchAnalyzer, SingleMatchAnalysis
from src.infrastructure.repositories.match_repository import HistoricalMatchRepository
from src.utils.stats_utils import round_to_precision
from src.domain.services.analysis_backtest_service import AnalysisBacktestService

router = APIRouter()
logger = get_logger("BacktestRouter")

class BacktestRequest(BaseModel):
    # Defaults set for 15 weeks leading up to Dec 29, 2025
    start_date: Optional[str] = "2025-09-15"
    end_date: Optional[str] = "2025-12-29"
    leagues: Optional[List[str]] = None
    min_confidence: float = 0.55

class BacktestResponse(BaseModel):
    total_matches: int
    analyzed_matches: int
    accuracy_rate: float
    correct_predictions: int
    market_accuracy: Dict[str, float]
    predictions: List[Dict[str, Any]]
    generated_at: str

@router.post("/run", response_model=BacktestResponse)
async def run_backtest(
    request: BacktestRequest,
    match_analyzer: MatchAnalyzer = Depends(get_match_analyzer),
    historical_matches: list = Depends(get_historical_matches),
):
    """
    Run backtest accuracy check using new analysis code (MatchAnalyzer).
    
    Default period: Last 15 weeks from 29.12.2025 (approx 15 Sept - 29 Dec).
    Skips AI analysis.
    """
    logger.info(f"Starting accuracy backtest from {request.start_date} to {request.end_date}")
    
    # 1. Parse Dates and Filter History
    start_dt = datetime.strptime(request.start_date, "%Y-%m-%d").date()
    end_dt = datetime.strptime(request.end_date, "%Y-%m-%d").date()
    
    # Create valid Match entities from historical dicts
    # We use a temp repo to convert ALL to entities first
    temp_repo = HistoricalMatchRepository(matches=historical_matches)
    all_entities = temp_repo.get_all()
    
    # Sort chronologically
    all_entities.sort(key=lambda m: m.match_date)
    
    target_matches = []
    
    for m in all_entities:
        if not m.match_date:
            continue
            
        if start_dt <= m.match_date <= end_dt:
            # Check filters
            if request.leagues and m.league not in request.leagues:
                continue
                
            # Must have result
            if m.fthg is None or m.ftag is None:
                continue
                
            target_matches.append(m)
            
    logger.info(f"Found {len(target_matches)} target matches for backtest")
    
    if not target_matches:
        return BacktestResponse(
            total_matches=0, analyzed_matches=0, accuracy_rate=0.0,
            correct_predictions=0, market_accuracy={}, predictions=[],
            generated_at=datetime.now().isoformat()
        )

    # 2. Run Backtest via Service
    service = AnalysisBacktestService(match_analyzer)
    result = service.run_backtest(target_matches, historical_matches)

    # 3. Format Response
    market_accuracies = {
        k: v["accuracy"] for k, v in result.market_stats.items()
    }
    
    return BacktestResponse(
        total_matches=result.total_matches,
        analyzed_matches=result.total_matches,
        accuracy_rate=round_to_precision(result.accuracy),
        correct_predictions=result.correct_predictions,
        market_accuracy=market_accuracies,
        predictions=result.predictions[:200], # Limit response size
        generated_at=datetime.now().isoformat()
    )
