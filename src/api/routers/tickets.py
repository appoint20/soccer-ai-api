"""
Tickets API Router.

Dedicated router for AI ticket generation functionality.
"""
from typing import Optional, Dict, Any

from fastapi import APIRouter, Depends, Query

from src.api.dependencies import (
    get_prediction_service,
    get_fixture_service,
    get_ticket_service,
    get_dixon_coles,
    get_match_stats_service,
    get_h2h_service,
    get_historical_matches,
)
from src.domain.services.prediction_service import PredictionService
from src.domain.services.fixture_service import FixtureService
from src.domain.services.ticket_service import TicketService
from src.domain.services.match_stats_service import MatchStatsService
from src.domain.services.h2h_service import H2HService
from src.statistics.dixon_coles_model import DixonColesModel
from src.data.storage.json_storage import JSONStorage
from src.utils.logger import get_logger

router = APIRouter(prefix="/tickets", tags=["Tickets"])
logger = get_logger("TicketsRouter")
json_storage = JSONStorage()
CACHE_DIR = "data/cache/analysis"


@router.get("/generate", response_model=Dict[str, Any])
async def generate_tickets(
    date: str = Query(..., description="Date of matches (YYYY-MM-DD)"),
    refresh: bool = Query(False, description="Force regenerate tickets"),
    custom_prompt: Optional[str] = None, 
    prediction_service: PredictionService = Depends(get_prediction_service),
    fixture_service: FixtureService = Depends(get_fixture_service),
    ticket_service: TicketService = Depends(get_ticket_service),
    dixon_coles: DixonColesModel = Depends(get_dixon_coles),
    match_stats_service: MatchStatsService = Depends(get_match_stats_service),
    h2h_service: H2HService = Depends(get_h2h_service),
    historical_matches: list = Depends(get_historical_matches),
):
    """
    Generate betting tickets using Gemini AI.
    """
    from src.domain.constants import GEMINI_TICKET_PROMPT
    
    analyses = []
    
    ticket_cache_path = f"data/cache/tickets/tickets_{date}.json"

    # 0. Check Ticket Cache First
    if not refresh:
        cached_tickets = json_storage.load(ticket_cache_path)
        if cached_tickets:
            logger.info(f"Loaded tickets from cache: {ticket_cache_path}")
            return cached_tickets

    # 1. Try Load Analysis from Cache First
    cache_path = f"{CACHE_DIR}/analysis_{date}.json"
    cached_data = json_storage.load(cache_path)
    
    if cached_data:
        logger.info(f"Loaded {len(cached_data)} matches from cache: {cache_path}")
        analyses = cached_data
    else:
        logger.info(f"Cache miss for {date}, running full analysis...")
        # 2. Fallback: Load & Analyze (Standard Flow)
        upcoming_matches = fixture_service.load_upcoming_fixtures(target_date=date)
        if not upcoming_matches:
            return {"error": "No matches found for date", "tickets": []}
            
        analyses = prediction_service.analyze_matches_for_date(
            matches=upcoming_matches,
            historical_matches=historical_matches,
            dixon_coles_model=dixon_coles,
            match_stats_service=match_stats_service,
            h2h_service=h2h_service
        )
    
    if not analyses:
         return {"error": "No analyses generated/found", "tickets": []}
    
    # 3. Prepare Prompt
    prompt = custom_prompt if custom_prompt else GEMINI_TICKET_PROMPT
    
    # 4. Call AI Ticket Service
    result = ticket_service.generate_tickets_ai(analyses, prompt)
    
    # 5. Save to Ticket Cache
    if result and "tickets" in result and result["tickets"]:
        if json_storage.save(result, ticket_cache_path):
            logger.info(f"Saved generated tickets to {ticket_cache_path}")
        else:
            logger.warning(f"Failed to save ticket cache to {ticket_cache_path}")

    # 6. Return result (Dict)
    return result
