"""
Tickets API Router.

Clean, simple ticket generation using AnalyzeMatchesUseCase.
"""
from typing import Dict, Any

from fastapi import APIRouter, Depends, Query, HTTPException

from src.api.dependencies import get_analyze_matches_use_case, ServiceContainer
from src.application.use_cases.analyze_matches import (
    AnalyzeMatchesUseCase,
    AnalyzeMatchesRequest,
)
from src.domain.services.ticket_service import TicketService
from src.utils.logger import get_logger

router = APIRouter(prefix="/tickets", tags=["Tickets"])
logger = get_logger("TicketsRouter")


def get_ticket_service() -> TicketService:
    """Get ticket service instance from ServiceContainer."""
    if ServiceContainer.ticket_service is None:
        raise HTTPException(status_code=500, detail="TicketService not initialized")
    return ServiceContainer.ticket_service


@router.get("/generate", response_model=Dict[str, Any])
async def generate_tickets(
    date: str = Query(..., description="Date of matches (YYYY-MM-DD)"),
    limit: int = Query(20, ge=1, le=50, description="Max matches to analyze"),
    use_case: AnalyzeMatchesUseCase = Depends(get_analyze_matches_use_case),
    ticket_service: TicketService = Depends(get_ticket_service),
):
    """
    Generate betting tickets using the analysis pipeline and AI.
    
    Uses AnalyzeMatchesUseCase for:
    - Poisson probabilities
    - Form analysis
    - H2H stats  
    - AI insights from Gemini 2.5 Pro
    """
    from src.domain.constants import GEMINI_TICKET_PROMPT
    
    # 1. Run analysis using the use case
    request = AnalyzeMatchesRequest(
        date=date,
        page=1,
        limit=limit,
        include_ai=True,
    )
    
    try:
        result = use_case.execute(request)
    except Exception as e:
        logger.error(f"Analysis failed: {e}")
        raise HTTPException(status_code=500, detail=f"Analysis failed: {e}")
    
    if not result.analyses:
        return {
            "date": date,
            "total_matches": 0,
            "error": "No matches found for date",
            "tickets": [],
        }
    
    # 2. Convert analyses to streamlined dict format for ticket service
    # Only include data the Gemini prompt actually uses
    analyses_for_tickets = []
    for analysis in result.analyses:
        match_data = {
            "match_id": analysis.match_id,
            "home_team": analysis.home_team,
            "away_team": analysis.away_team,
            "date": analysis.date,
            "time": analysis.time,
            "league": analysis.league,
            # Poisson probabilities - primary data source for predictions
            "poisson": analysis.poisson.to_dict(),
            # Monte Carlo adjusted probabilities
            "monte_carlo": {
                "over_25": analysis.monte_carlo.over_25.to_dict(),
                "btts": analysis.monte_carlo.btts.to_dict(),
                "home_win": analysis.monte_carlo.home_win.to_dict(),
                "away_win": analysis.monte_carlo.away_win.to_dict(),
                "draw": analysis.monte_carlo.draw.to_dict(),
            },
            # Form data for context
            "home_form": analysis.home_last_5.to_dict(),
            "away_form": analysis.away_last_5.to_dict(),
            # H2H for historical context
            "h2h": analysis.h2h_stats.to_dict(),
        }
        
        # Include AI analysis if available - this is critical for ticket selection
        if analysis.ai_analysis:
            match_data["ai_analysis"] = {
                "best_prediction": analysis.ai_analysis.best_prediction,
                "reason": analysis.ai_analysis.reason,
                "confidence_level": analysis.ai_analysis.confidence_level,
            }
        
        # Include odds if available - critical for ticket value assessment
        if analysis.odds:
            match_data["odds"] = analysis.odds
        
        analyses_for_tickets.append(match_data)
    
    # 3. Generate tickets using AI
    tickets_result = ticket_service.generate_tickets_ai(
        analyses_for_tickets,
        GEMINI_TICKET_PROMPT,
    )
    
    return {
        "date": date,
        "total_matches": len(analyses_for_tickets),
        "generated_at": result.generated_at,
        **tickets_result,
    }
