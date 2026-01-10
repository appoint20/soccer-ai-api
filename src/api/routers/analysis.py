"""
Match Analysis API Router.

This is a THIN CONTROLLER - it only handles HTTP concerns.
All business logic is delegated to the AnalyzeMatchesUseCase.
"""
from datetime import datetime, date
from typing import Optional
import re

from fastapi import APIRouter, Depends, Query, HTTPException

from src.api.schemas import (
    AnalyzeResponse,
    BacktestResult,
    BacktestStats,
)
from src.api.dependencies import (
    get_analyze_matches_use_case,
    get_backtest_predictions_use_case,
)
from src.api.presenters.match_analysis_presenter import MatchAnalysisPresenter
from src.application.use_cases.analyze_matches import (
    AnalyzeMatchesUseCase,
    AnalyzeMatchesRequest,
)
from src.application.use_cases.backtest_predictions import (
    BacktestPredictionsUseCase,
    BacktestRequest,
)
from src.utils.logger import get_logger

router = APIRouter(prefix="/matches", tags=["Analysis"])
logger = get_logger("AnalysisRouter")

# Constants
DATE_PATTERN = re.compile(r"^\d{4}-\d{2}-\d{2}$")


@router.get("/analyze", response_model=AnalyzeResponse)
async def analyze_matches(
    date: str = Query(..., description="Date of matches (YYYY-MM-DD)"),
    page: int = Query(1, ge=1, description="Page number"),
    limit: int = Query(50, ge=1, le=100, description="Items per page"),
    refresh: bool = Query(False, description="Force refresh cache"),
    use_case: AnalyzeMatchesUseCase = Depends(get_analyze_matches_use_case),
) -> AnalyzeResponse:
    """
    Analyze upcoming matches with probability models and AI insights.
    
    Supports pagination and historical backtesting.
    """
    # 1. Parse and validate date
    parsed_date = _validate_and_parse_date(date)
    if not parsed_date:
         # Should be caught by validation, but double check
         raise HTTPException(status_code=400, detail="Invalid date")

    # 2. Create request
    analyze_request = AnalyzeMatchesRequest(
        date=parsed_date,
        page=page,
        limit=limit,
        refresh=refresh,
    )
    
    # 3. Execute Analysis Use Case
    try:
        result = use_case.execute(analyze_request)
    except Exception as e:
        logger.error(f"Analysis failed: {e}", exc_info=True)
        raise HTTPException(status_code=500, detail=str(e))

    # 4. Handle Backtesting (if past date)
    # The UseCase now handles backtesting via injected service
    is_backtest_date = _is_past_date(parsed_date)
    
    # Extract stats from result (UseCase now populates this)
    # Note: result.backtest_stats is a dict, we need to convert to Pydantic if needed
    backtest_stats = None
    if result.backtest_stats:
        # Convert dict to BacktestStats schema
        backtest_stats = BacktestStats(**result.backtest_stats)

    # 5. Map to API Response (Presenter)
    items = []
    for analysis in result.analyses:
        # Presenter mapping (backtest result is now inside analysis)
        response_item = MatchAnalysisPresenter.to_response(analysis)
        
        # Security: Verify object identity hasn't drifted
        if response_item.match_id != analysis.match_id:
             logger.critical(f"Object divergence detected! {response_item.match_id} != {analysis.match_id}")
             raise HTTPException(status_code=500, detail="Internal data integrity error")
             
        items.append(response_item)

    return AnalyzeResponse(
        items=items,
        total=result.total,
        page=result.page,
        limit=result.limit,
        generated_at=result.generated_at,
        is_past_date=is_backtest_date,
        backtest_stats=backtest_stats,
    )


def _validate_and_parse_date(date_str: Optional[str]) -> Optional[date]:
    """Validate and parse date string."""
    if date_str is None:
        return None
    
    if not DATE_PATTERN.match(date_str):
        raise HTTPException(status_code=400, detail=f"Invalid date format '{date_str}'. Expected YYYY-MM-DD.")
    
    try:
        return datetime.strptime(date_str, "%Y-%m-%d").date()
    except ValueError:
        raise HTTPException(status_code=400, detail=f"Invalid date '{date_str}'. Not a valid calendar date.")

def _is_past_date(d: date) -> bool:
    """Check if date is in the past."""
    return d < datetime.now().date()
