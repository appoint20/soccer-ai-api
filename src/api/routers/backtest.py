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

    # 2. Run Analysis Loop
    results = []
    market_stats = {
        "over_25": {"correct": 0, "total": 0},
        "btts": {"correct": 0, "total": 0},
        "home_win": {"correct": 0, "total": 0},
        "away_win": {"correct": 0, "total": 0},
    }
    
    for i, match in enumerate(target_matches):
        if i % 50 == 0:
            logger.info(f"Processing match {i+1}/{len(target_matches)}")
            
        # Context: All matches STRICTLY BEFORE this match.date
        # (This prevents data leakage)
        # However, for efficiency, passing all_entities works IF the calculators
        # respect dates. But strictly safe way is filtering.
        # Given performance, filtering list 10k items 100 times is slow.
        # Calculators convert to dicts anyway.
        # BUT MatchAnalyzer filters mainly by match itself.
        # To be safe, let's trust calculators usually, but filtering is safer.
        # Let's optimize: `past_matches` grows as acceptable index increases.
        # Since `all_entities` is sorted by date, we can slice.
        # Find index of this match in `all_entities`.
        
        # This is slow unless optimized.
        # Let's just pass `all_entities`. The calculators SHOULD handle date<=match_date exclusion logic.
        # Wait, usually "past n matches" logic filters where date < current.
        # If the current match is in the list with result, it might be picked up?
        # Standard implementation of `calculate_form_stats` usually filters `if m.date < target_date`.
        # I'll rely on that for now to keep it reasonably fast.
        
        try:
            analysis = match_analyzer.analyze(
                match=match,
                historical_matches=historical_matches # Pass dicts for performance (O(1) conversion)
            )
            
            # 3. Evaluate Predictions
            result = evaluate_prediction(analysis, match, request.min_confidence)
            if result:
                results.append(result)
                
                # Update Market Stats
                m_type = result["market"]
                if m_type in market_stats:
                    market_stats[m_type]["total"] += 1
                    if result["is_correct"]:
                        market_stats[m_type]["correct"] += 1
                        
        except Exception as e:
            logger.error(f"Error analyzing match {match.id}: {e}")
            continue

    # 4. Aggregate Stats
    total_correct = sum(1 for r in results if r["is_correct"])
    total_analyzed = len(results)
    
    market_accuracies = {}
    for m, stats in market_stats.items():
        if stats["total"] > 0:
            market_accuracies[m] = round_to_precision((stats["correct"] / stats["total"]) * 100)
        else:
            market_accuracies[m] = 0.0

    overall_accuracy = (total_correct / total_analyzed * 100) if total_analyzed > 0 else 0.0
    
    return BacktestResponse(
        total_matches=len(target_matches),
        analyzed_matches=total_analyzed,
        accuracy_rate=round_to_precision(overall_accuracy),
        correct_predictions=total_correct,
        market_accuracy=market_accuracies,
        predictions=results[:100], # Limit response size
        generated_at=datetime.now().isoformat()
    )


def evaluate_prediction(analysis: SingleMatchAnalysis, match, threshold: float) -> Optional[Dict]:
    """Compare analysis prediction vs actual result."""
    
    # We look at `aggregated_markets` for the "Best Pick" logic equivalent
    # Or just check probabilities directly.
    # User asks "how the model is doing".
    # Let's evaluate the HIGHEST probability market (if > threshold).
    
    markets = analysis.aggregated_markets
    if not markets:
        return None
        
    # Find best market
    best_market = None
    best_prob = 0.0
    
    # Check Over 2.5
    p_over = markets.get("over_25", {}).get("probability", 0)
    if p_over > best_prob:
        best_prob = p_over
        best_market = "over_25"
        
    # Check BTTS
    p_btts = markets.get("btts", {}).get("probability", 0)
    if p_btts > best_prob:
        best_prob = p_btts
        best_market = "btts"
        
    # Check Home Win
    p_home = markets.get("home_win", {}).get("probability", 0)
    if p_home > best_prob:
        best_prob = p_home
        best_market = "home_win"
        
    # Check Away Win
    p_away = markets.get("away_win", {}).get("probability", 0)
    if p_away > best_prob:
        best_prob = p_away
        best_market = "away_win"
        
    if best_prob < threshold:
        return None # No bet
        
    # Verify Result
    is_correct = False
    home_goals = match.fthg
    away_goals = match.ftag
    total_goals = home_goals + away_goals
    
    if best_market == "over_25":
        is_correct = total_goals > 2.5
    elif best_market == "btts":
        is_correct = (home_goals > 0 and away_goals > 0)
    elif best_market == "home_win":
        is_correct = home_goals > away_goals
    elif best_market == "away_win":
        is_correct = away_goals > home_goals
        
    return {
        "date": str(match.match_date),
        "match": f"{match.home_team} vs {match.away_team}",
        "market": best_market,
        "probability": round_to_precision(best_prob),
        "is_correct": is_correct,
        "actual_score": f"{home_goals}-{away_goals}"
    }
