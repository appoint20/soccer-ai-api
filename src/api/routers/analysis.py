"""
Match Analysis API Router.

This is a THIN CONTROLLER - it only handles HTTP concerns.
All business logic is delegated to the AnalyzeMatchesUseCase.

Clean Architecture Principles:
- No business logic in router
- Uses dependency injection
- Easy to test (mock the use case)
- Single responsibility: HTTP request/response handling
"""
from datetime import datetime
from typing import Optional
import re

from fastapi import APIRouter, Depends, Query, HTTPException

from src.api.schemas import (
    AnalyzeResponse,
    MatchAnalysis,
    TeamFormStats,
    H2HStats,
    PoissonProbabilities,
    MonteCarloMarketResult,
    MonteCarloResults,
    AIAnalysis,
)
from src.api.dependencies import get_analyze_matches_use_case
from src.application.use_cases.analyze_matches import (
    AnalyzeMatchesUseCase,
    AnalyzeMatchesRequest,
    SingleMatchAnalysis,
)
from src.utils.logger import get_logger

router = APIRouter(prefix="/matches", tags=["Analysis"])
logger = get_logger("AnalysisRouter")

# Date format validation
DATE_PATTERN = re.compile(r"^\d{4}-\d{2}-\d{2}$")


@router.get("/analyze", response_model=AnalyzeResponse)
async def analyze_matches(
    date: Optional[str] = Query(None, description="Date of matches (YYYY-MM-DD)"),
    page: int = Query(1, ge=1, description="Page number"),
    limit: int = Query(50, ge=1, le=200, description="Items per page"),
    include_ai: bool = Query(True, description="Include AI analysis"),
    use_case: AnalyzeMatchesUseCase = Depends(get_analyze_matches_use_case),
) -> AnalyzeResponse:
    """
    Analyze upcoming matches with statistical models.
    
    Returns comprehensive analysis including:
    - Team form statistics (last 5 overall, last 3 venue-specific)
    - Head-to-head history with reliability score
    - Poisson goal probabilities with Dixon-Coles correction
    - Monte Carlo uncertainty adjustments (capped at ±7%)
    - Overall confidence score (0-100)
    - AI predictions (optional)
    
    All calculations use proper dependency injection and clean architecture.
    """
    # Validate date format
    if date is not None:
        if not DATE_PATTERN.match(date):
            raise HTTPException(
                status_code=400,
                detail=f"Invalid date format '{date}'. Expected YYYY-MM-DD."
            )
        # Validate it's a real date
        try:
            datetime.strptime(date, "%Y-%m-%d")
        except ValueError:
            raise HTTPException(
                status_code=400,
                detail=f"Invalid date '{date}'. Not a valid calendar date."
            )
    
    # Build request DTO
    request = AnalyzeMatchesRequest(
        date=date,
        page=page,
        limit=limit,
        include_ai=include_ai,
    )
    
    # Execute use case (all business logic is here)
    result = use_case.execute(request)
    
    # Convert domain objects to API response
    items = [_to_api_response(analysis) for analysis in result.analyses]
    
    return AnalyzeResponse(
        items=items,
        total=result.total,
        page=result.page,
        limit=result.limit,
        generated_at=result.generated_at,
    )


def _to_api_response(analysis: SingleMatchAnalysis) -> MatchAnalysis:
    """Convert domain SingleMatchAnalysis to API MatchAnalysis schema."""
    return MatchAnalysis(
        match_id=analysis.match_id,
        home_team=analysis.home_team,
        away_team=analysis.away_team,
        date=analysis.date,
        time=analysis.time,
        league=analysis.league,
        home_last_5=TeamFormStats(**analysis.home_last_5.to_dict()),
        away_last_5=TeamFormStats(**analysis.away_last_5.to_dict()),
        home_last_3_home=TeamFormStats(**analysis.home_last_3_home.to_dict()),
        away_last_3_away=TeamFormStats(**analysis.away_last_3_away.to_dict()),
        h2h_last_5=H2HStats(**analysis.h2h_stats.to_dict()),
        poisson=PoissonProbabilities(**analysis.poisson.to_dict()),
        monte_carlo=MonteCarloResults(
            over_25=MonteCarloMarketResult(**analysis.monte_carlo.over_25.to_dict()),
            btts=MonteCarloMarketResult(**analysis.monte_carlo.btts.to_dict()),
            home_win=MonteCarloMarketResult(**analysis.monte_carlo.home_win.to_dict()),
            away_win=MonteCarloMarketResult(**analysis.monte_carlo.away_win.to_dict()),
            draw=MonteCarloMarketResult(**analysis.monte_carlo.draw.to_dict()),
            goals_2_3=MonteCarloMarketResult(**analysis.monte_carlo.goals_2_3.to_dict()),
        ),
        overall_confidence=analysis.overall_confidence,
        ai_analysis=_to_ai_analysis_schema(analysis.ai_analysis) if analysis.ai_analysis else None,
    )


def _to_ai_analysis_schema(ai: "AIAnalysis") -> AIAnalysis:
    """Convert AIAnalysis dataclass to API schema."""
    return AIAnalysis(
        best_prediction=ai.best_prediction,
        reason=ai.reason,
        short_analysis=ai.short_analysis,
        confidence_level=ai.confidence_level,
    )
