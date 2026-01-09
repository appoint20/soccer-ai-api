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
    refresh: bool = Query(False, description="Force refresh AI analysis (bypass cache)"),
    use_case: AnalyzeMatchesUseCase = Depends(get_analyze_matches_use_case),
) -> AnalyzeResponse:
    """
    Analyze upcoming matches with statistical models and AI insights.
    
    Returns comprehensive analysis including:
    - Team form statistics (last 5 overall, last 3 venue-specific)
    - Head-to-head history with reliability score
    - Poisson goal probabilities with Dixon-Coles correction
    - Monte Carlo uncertainty adjustments
    - Overall confidence score (0-100)
    - AI predictions from Gemini 2.5 Pro (cached by date)
    
    Use refresh=true to force new AI analysis (bypasses cache).
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
    
    # Build request DTO - AI always enabled
    request = AnalyzeMatchesRequest(
        date=date,
        page=page,
        limit=limit,
        include_ai=True,
        refresh=refresh,  # Pass refresh to bypass AI cache
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
    from src.api.schemas import TeamStats, VenueFormStats, MatchAnalysisResult, MatchAnalysisMarket
    
    # Extract enrichment data
    ed = analysis.enrichment_data or {}
    home_ed = ed.get("home", {})
    away_ed = ed.get("away", {})
    
    # Build VenueFormStats for home
    home_venue = home_ed.get("venue_form", {})
    home_venue_form = VenueFormStats(
        form_string=home_venue.get("form_string", ""),
        matches_played=home_venue.get("matches_played", 0),
        wins=home_venue.get("wins", 0),
        draws=home_venue.get("draws", 0),
        losses=home_venue.get("losses", 0),
        goals_scored=home_venue.get("goals_scored", 0),
        goals_conceded=home_venue.get("goals_conceded", 0),
        points=home_venue.get("points", 0),
        avg_goals_scored=home_venue.get("avg_goals_scored", 0.0),
        avg_goals_conceded=home_venue.get("avg_goals_conceded", 0.0),
        over_25_rate=analysis.home_last_3_home.over_25_rate,
        btts_rate=analysis.home_last_3_home.btts_rate,
        win_rate=analysis.home_last_3_home.win_rate,
        lose_rate=analysis.home_last_3_home.lose_rate,
        draw_rate=analysis.home_last_3_home.draw_rate,
        goals_2_3_rate=analysis.home_last_3_home.goals_2_3_rate,
    )
    
    # Build VenueFormStats for away
    away_venue = away_ed.get("venue_form", {})
    away_venue_form = VenueFormStats(
        form_string=away_venue.get("form_string", ""),
        matches_played=away_venue.get("matches_played", 0),
        wins=away_venue.get("wins", 0),
        draws=away_venue.get("draws", 0),
        losses=away_venue.get("losses", 0),
        goals_scored=away_venue.get("goals_scored", 0),
        goals_conceded=away_venue.get("goals_conceded", 0),
        points=away_venue.get("points", 0),
        avg_goals_scored=away_venue.get("avg_goals_scored", 0.0),
        avg_goals_conceded=away_venue.get("avg_goals_conceded", 0.0),
        over_25_rate=analysis.away_last_3_away.over_25_rate,
        btts_rate=analysis.away_last_3_away.btts_rate,
        win_rate=analysis.away_last_3_away.win_rate,
        lose_rate=analysis.away_last_3_away.lose_rate,
        draw_rate=analysis.away_last_3_away.draw_rate,
        goals_2_3_rate=analysis.away_last_3_away.goals_2_3_rate,
    )
    
    # Build TeamStats
    homeStats = TeamStats(
        form=home_ed.get("form", ""),
        form_points=home_ed.get("form_points", 0),
        position=home_ed.get("position", 0),
        points=home_ed.get("points", 0),
        last_5_overall=TeamFormStats(**analysis.home_last_5.to_dict()),
        venue_form=home_venue_form,
    )
    
    awayStats = TeamStats(
        form=away_ed.get("form", ""),
        form_points=away_ed.get("form_points", 0),
        position=away_ed.get("position", 0),
        points=away_ed.get("points", 0),
        last_5_overall=TeamFormStats(**analysis.away_last_5.to_dict()),
        venue_form=away_venue_form,
    )
    
    # Build match_analysis from aggregated_markets
    match_analysis = None
    if analysis.aggregated_markets:
        am = analysis.aggregated_markets
        match_analysis = MatchAnalysisResult(
            over_25=MatchAnalysisMarket(**{k: v for k, v in am.get("over_25", {}).items() if k in ["probability", "probability_pct", "confidence", "verdict"]}),
            btts=MatchAnalysisMarket(**{k: v for k, v in am.get("btts", {}).items() if k in ["probability", "probability_pct", "confidence", "verdict"]}),
            goals_2_3=MatchAnalysisMarket(**{k: v for k, v in am.get("goals_2_3", {}).items() if k in ["probability", "probability_pct", "confidence", "verdict"]}),
            home_win=MatchAnalysisMarket(**{k: v for k, v in am.get("home_win", {}).items() if k in ["probability", "probability_pct", "confidence", "verdict"]}),
            away_win=MatchAnalysisMarket(**{k: v for k, v in am.get("away_win", {}).items() if k in ["probability", "probability_pct", "confidence", "verdict"]}),
            draw=MatchAnalysisMarket(**{k: v for k, v in am.get("draw", {}).items() if k in ["probability", "probability_pct", "confidence", "verdict"]}),
            confidence_index=am.get("confidence_index", 0),
        )
    
    return MatchAnalysis(
        match_id=analysis.match_id,
        home_team=analysis.home_team,
        away_team=analysis.away_team,
        date=analysis.date,
        time=analysis.time,
        league=analysis.league,
        matchday=ed.get("matchday", 0) + 1,  # +1 for upcoming match
        position_difference=ed.get("position_difference", 0),
        points_difference=ed.get("points_difference", 0),
        homeStats=homeStats,
        awayStats=awayStats,
        h2h_last_5=H2HStats(**analysis.h2h_stats.to_dict()),
        poisson=PoissonProbabilities(**analysis.poisson.to_dict()),
        monte_carlo=MonteCarloResults(
            over_25_probability=analysis.monte_carlo.over_25.adjusted_probability,
            btts_probability=analysis.monte_carlo.btts.adjusted_probability,
            home_win_probability=analysis.monte_carlo.home_win.adjusted_probability,
            away_win_probability=analysis.monte_carlo.away_win.adjusted_probability,
            draw_probability=analysis.monte_carlo.draw.adjusted_probability,
            goals_2_3_probability=analysis.monte_carlo.goals_2_3.adjusted_probability,
        ),
        overall_confidence=analysis.overall_confidence,
        ai_analysis=_to_ai_analysis_schema(analysis.ai_analysis) if analysis.ai_analysis else None,
        match_analysis=match_analysis,
    )


def _to_ai_analysis_schema(ai: "AIAnalysis") -> AIAnalysis:
    """Convert AIAnalysis dataclass to API schema."""
    return AIAnalysis(
        best_prediction=ai.best_prediction,
        reason=ai.reason,
        short_analysis=ai.short_analysis,
        confidence_level=ai.confidence_level,
    )
