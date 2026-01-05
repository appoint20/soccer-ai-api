from datetime import date, datetime, timedelta
from typing import List, Optional, Dict, Any

from fastapi import APIRouter, Depends, Query

from src.api.dependencies import (
    get_dixon_coles,
    get_h2h_service,
    get_historical_matches,
    get_match_stats_service,
    get_prediction_service,
    get_fixture_service,
    get_ticket_service,
    get_gemini_service
)
from src.api.schemas import (
    Ticket,
    AnalyzeMatchesResponse,
    GenerateTicketsResponse
)
from src.domain.services.h2h_service import H2HService
from src.domain.services.match_stats_service import MatchStatsService
from src.domain.services.prediction_service import PredictionService
from src.domain.services.fixture_service import FixtureService
from src.domain.services.ticket_service import TicketService
from src.domain.services.gemini_service import GeminiService
from src.statistics.dixon_coles_model import DixonColesModel
from src.utils.logger import get_logger

from src.data.storage.json_storage import JSONStorage

router = APIRouter()
logger = get_logger("AnalysisRouter")
json_storage = JSONStorage()
CACHE_DIR = "data/cache/analysis"


@router.get("/matches", response_model=AnalyzeMatchesResponse)
async def analyze_matches(
    date: Optional[str] = Query(None, description="Date of matches (YYYY-MM-DD)"),
    refresh: bool = Query(False, description="Force refresh cache"),
    page: int = Query(1, ge=1, description="Page number"),
    limit: int = Query(100, ge=1, le=1000, description="Items per page"),
    prediction_service: PredictionService = Depends(get_prediction_service),
    fixture_service: FixtureService = Depends(get_fixture_service),
    dixon_coles: DixonColesModel = Depends(get_dixon_coles),
    match_stats_service: MatchStatsService = Depends(get_match_stats_service),
    h2h_service: H2HService = Depends(get_h2h_service),
    gemini_service: GeminiService = Depends(get_gemini_service),
    historical_matches: list = Depends(get_historical_matches),
):
    """
    Get detailed analysis for all matches on a specific date.
    """
    logger.info(f"Analyzing matches for date: {date}")
    
    # 0. Check Cache First
    cache_path = None
    cached_data = None
    
    if date and not refresh:
        cache_path = f"{CACHE_DIR}/analysis_{date}.json"
        cached_data = json_storage.load(cache_path)
    else:
        cached_data = None  # No date = no specific cache file to read

    if cached_data:
        logger.info(f"Loaded {len(cached_data)} matches from cache: {cache_path}")
        analyses = cached_data
        
        # Pagination on cached data
        total = len(analyses)
        offset = (page - 1) * limit
        paginated_items = analyses[offset : offset + limit]
        
        # No enrichment needed (already enriched in cache)
        
    else:
        logger.info(f"Cache miss for {date}, running analysis...")
    
        # 1. Load Fixtures (Load ALL upcoming, then filter)
        upcoming_matches = fixture_service.load_upcoming_fixtures(target_date=None)
        
        # FILTER BY REQUESTED DATE (Critical Fix)
        if date:
            filtered_matches = []
            for m in upcoming_matches:
                # m['match_date'] is YYYY-MM-DD string from FixtureService
                if m.get("match_date") == date:
                    filtered_matches.append(m)
            upcoming_matches = filtered_matches
            logger.info(f"Filtered to {len(upcoming_matches)} matches for date {date}")
        
        if not upcoming_matches:
            logger.info(f"No matches found for date {date} in fixtures.")
            return AnalyzeMatchesResponse(
                items=[],
                total=0,
                offset=0,
                limit=limit,
                generated_at=datetime.now().isoformat()
            )
    
        # 2. Run Analysis
        analyses = prediction_service.analyze_matches_for_date(
            matches=upcoming_matches,
            historical_matches=historical_matches,
            dixon_coles_model=dixon_coles,
            match_stats_service=match_stats_service,
            h2h_service=h2h_service
        )
        
        # 3. Pagination
        total = len(analyses)
        offset = (page - 1) * limit
        paginated_items = analyses[offset : offset + limit]
        
        # 4. AI Enrichment (Mandatory)
        from src.domain.constants import GEMINI_ANALYSIS_PROMPT
        logger.info(f"Enriching {len(paginated_items)} matches with AI insights")
        paginated_items = gemini_service.enrich_matches(paginated_items, GEMINI_ANALYSIS_PROMPT)
        
        # 5. Save to Cache (If date is provided)
        if date and paginated_items:
            # Note: We save the paginated_items. Since default limit=100 and matches=66, this saves ALL.
            # If limit < matches, we save partial. This is known behavior. 
            cache_path = f"{CACHE_DIR}/analysis_{date}.json"
            if json_storage.save(paginated_items, cache_path):
                 logger.info(f"Saved analysis cache to {cache_path}")
            else:
                 logger.warning(f"Failed to save analysis cache to {cache_path}")

    return AnalyzeMatchesResponse(
        items=paginated_items,
        total=total,
        offset=offset,
        limit=limit,
        generated_at=datetime.now().isoformat()
    )


@router.get("/tickets/generate", response_model=Dict[str, Any])
async def generate_tickets(
    date: str = Query(..., description="Date of matches (YYYY-MM-DD)"),
    refresh: bool = Query(False, description="Force regenerate tickets"),
    # min_odds/max_odds/min_confidence ignored or passed to prompt if advanced
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
    Generate betting tickets using Gemini AI (Standard Endpoint Replaced).
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
    # Pass analyses directly (List[Dict])
    result = ticket_service.generate_tickets_ai(analyses, prompt)
    
    # 5. Save to Ticket Cache
    if result and "tickets" in result and result["tickets"]:
        if json_storage.save(result, ticket_cache_path):
            logger.info(f"Saved generated tickets to {ticket_cache_path}")
        else:
            logger.warning(f"Failed to save ticket cache to {ticket_cache_path}")

    # 6. Return result (Dict)
    return result