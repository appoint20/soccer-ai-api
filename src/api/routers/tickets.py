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
    date: str = Query(..., description="Start date for ticket generation (YYYY-MM-DD)"),
    days: int = Query(3, ge=1, le=7, description="Number of days to include (e.g., 3 for Fri-Sun)"),
    limit_per_day: int = Query(20, ge=1, le=50, description="Max matches per day"),
    use_case: AnalyzeMatchesUseCase = Depends(get_analyze_matches_use_case),
    ticket_service: TicketService = Depends(get_ticket_service),
):
    """
    Generate betting tickets for multiple days (e.g., weekend: Fri-Sun-Mon).

    If it's Friday, gathers matches for Fri, Sat, Sun, and optionally Mon.
    Analyzes all matches from supported leagues' current matchday and generates
    1-5 tickets, each containing 3 matches with high confidence.

    Uses AnalyzeMatchesUseCase for:
    - Poisson probabilities
    - Form analysis
    - H2H stats
    - AI insights from Gemini 2.5 Pro
    """
    from datetime import datetime, timedelta
    from src.domain.constants import GEMINI_TICKET_PROMPT

    # Parse start date
    try:
        start_date = datetime.strptime(date, "%Y-%m-%d").date()
    except ValueError:
        raise HTTPException(status_code=400, detail="Invalid date format. Use YYYY-MM-DD")

    # Gather analyses for multiple days
    all_analyses = []
    dates_analyzed = []

    for day_offset in range(days):
        current_date = start_date + timedelta(days=day_offset)
        date_str = current_date.strftime("%Y-%m-%d")
        dates_analyzed.append(date_str)

        logger.info(f"Analyzing matches for {date_str}")

        # Run analysis for this day
        request = AnalyzeMatchesRequest(
            date=current_date,
            page=1,
            limit=limit_per_day,
            refresh=False,  # Use cached AI analysis if available
        )

        try:
            result = use_case.execute(request)
            if result.analyses:
                all_analyses.extend(result.analyses)
                logger.info(f"Found {len(result.analyses)} matches for {date_str}")
        except Exception as e:
            logger.error(f"Analysis failed for {date_str}: {e}")
            # Continue with other days even if one fails
            continue
    
    if not all_analyses:
        return {
            "start_date": date,
            "days_analyzed": dates_analyzed,
            "total_matches": 0,
            "error": "No matches found for the specified date range",
            "tickets": [],
        }
    
    logger.info(f"Total matches gathered across {len(dates_analyzed)} days: {len(all_analyses)}")

    # 2. Convert analyses to streamlined dict format for ticket service
    # Only include data the Gemini prompt actually uses
    analyses_for_tickets = []
    for analysis in all_analyses:
        match_data = {
            "match_id": analysis.match_id,
            "home_team": analysis.home_team,
            "away_team": analysis.away_team,
            "date": analysis.date,
            "time": analysis.time,
            "league": analysis.league,
            # Poisson probabilities - primary data source for predictions
            "poisson": analysis.poisson,
            # Monte Carlo adjusted probabilities
            "monte_carlo": analysis.monte_carlo,
            # Form data for context
            "homeStats": analysis.homeStats,
            "awayStats": analysis.awayStats,
            # H2H for historical context
            "h2h_last_5": analysis.h2h_last_5,
        }
        
        # Include AI analysis if available - this is critical for ticket selection
        if analysis.ai_analysis:
            # Handle both dict (from cache) and object
            if isinstance(analysis.ai_analysis, dict):
                match_data["ai_analysis"] = {
                    "best_prediction": analysis.ai_analysis.get("best_prediction"),
                    "reason": analysis.ai_analysis.get("reason"),
                    "confidence_level": analysis.ai_analysis.get("confidence_level"),
                }
            else:
                match_data["ai_analysis"] = {
                    "best_prediction": getattr(analysis.ai_analysis, "best_prediction", None),
                    "reason": getattr(analysis.ai_analysis, "reason", None),
                    "confidence_level": getattr(analysis.ai_analysis, "confidence_level", None),
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
        "start_date": date,
        "days_analyzed": dates_analyzed,
        "total_matches": len(analyses_for_tickets),
        "generated_at": datetime.now().isoformat(),
        **tickets_result,
    }
