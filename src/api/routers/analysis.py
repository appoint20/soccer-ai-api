from datetime import date, datetime
from typing import List, Optional

from fastapi import APIRouter, Depends, HTTPException, Query

from src.api.dependencies import (
    get_comprehensive_service,
    get_dixon_coles,
    get_h2h_service,
    get_historical_matches,
    get_match_stats_service,
    get_prediction_service,
    get_fixture_service,
    get_ticket_service
)
from src.api.schemas import (
    AnalysisResponse,
    MatchAnalysisDto,
    Ticket,
    WeeklyTicket
)
from src.domain.services.comprehensive_analysis_service import ComprehensiveAnalysisService
from src.domain.services.h2h_service import H2HService
from src.domain.services.match_stats_service import MatchStatsService
from src.domain.services.prediction_service import PredictionService
from src.domain.services.fixture_service import FixtureService
from src.domain.services.ticket_service import TicketService
from src.statistics.dixon_coles_model import DixonColesModel
from src.utils.logger import get_logger

router = APIRouter()
logger = get_logger("AnalysisRouter")


@router.get("/matches", response_model=List[MatchAnalysisDto])
async def analyze_matches(
    date: str = Query(..., description="Date to analyze (YYYY-MM-DD)"),
    prediction_service: PredictionService = Depends(get_prediction_service),
    fixture_service: FixtureService = Depends(get_fixture_service),
    comprehensive_service: ComprehensiveAnalysisService = Depends(get_comprehensive_service),
    dixon_coles: DixonColesModel = Depends(get_dixon_coles),
    match_stats_service: MatchStatsService = Depends(get_match_stats_service),
    h2h_service: H2HService = Depends(get_h2h_service),
    historical_matches: list = Depends(get_historical_matches),
):
    """
    Analyze all matches for a specific date.
    
    Orchestrates data loading, feature engineering, ML prediction,
    and statistical analysis (Poisson, H2H).
    """
    logger.info(f"Analyzing matches for date: {date}")
    
    # 1. Load Fixtures (via Service)
    upcoming_matches = fixture_service.load_upcoming_fixtures(target_date=date)
    
    if not upcoming_matches:
        logger.warning(f"No matches found for date {date}")
        return []

    # 2. Run Analysis (via Service)
    analyses = prediction_service.analyze_matches_for_date(
        matches=upcoming_matches,
        historical_matches=historical_matches,
        comprehensive_service=comprehensive_service,
        dixon_coles_model=dixon_coles,
        match_stats_service=match_stats_service,
        h2h_service=h2h_service
    )
    
    return analyses


@router.get("/comprehensive", response_model=AnalysisResponse)
async def comprehensive_analysis(
    date: str = Query(..., description="Date to analyze (YYYY-MM-DD)"),
    prediction_service: PredictionService = Depends(get_prediction_service),
    fixture_service: FixtureService = Depends(get_fixture_service),
    comprehensive_service: ComprehensiveAnalysisService = Depends(get_comprehensive_service),
    dixon_coles: DixonColesModel = Depends(get_dixon_coles),
    match_stats_service: MatchStatsService = Depends(get_match_stats_service),
    h2h_service: H2HService = Depends(get_h2h_service),
    historical_matches: list = Depends(get_historical_matches),
):
    """
    Get comprehensive analysis including high-confidence picks.
    """
    # 1. Load & Analyze (Reusing logic via services)
    upcoming_matches = fixture_service.load_upcoming_fixtures(target_date=date)
    
    if not upcoming_matches:
        return AnalysisResponse(
            date=date,
            total_matches=0,
            matches=[],
            summary="No matches found."
        )

    analyses = prediction_service.analyze_matches_for_date(
        matches=upcoming_matches,
        historical_matches=historical_matches,
        comprehensive_service=comprehensive_service,
        dixon_coles_model=dixon_coles,
        match_stats_service=match_stats_service,
        h2h_service=h2h_service
    )
    
    # 2. Extract Top Picks (Could be moved to a service too, but simple enough here for now)
    # Or reuse TicketService logic if it fits
    
    return AnalysisResponse(
        date=date,
        total_matches=len(analyses),
        matches=analyses,
        summary=f"Analysis complete for {len(analyses)} matches."
    )


@router.get("/tickets/generate", response_model=List[Ticket])
async def generate_tickets(
    date: str = Query(..., description="Date (YYYY-MM-DD)"),
    min_odds: float = Query(1.60, ge=1.0),
    max_odds: float = Query(2.80, ge=1.0),
    prediction_service: PredictionService = Depends(get_prediction_service),
    fixture_service: FixtureService = Depends(get_fixture_service),
    ticket_service: TicketService = Depends(get_ticket_service),
    # ... other dependencies needed for analysis ...
    comprehensive_service: ComprehensiveAnalysisService = Depends(get_comprehensive_service),
    dixon_coles: DixonColesModel = Depends(get_dixon_coles),
    match_stats_service: MatchStatsService = Depends(get_match_stats_service),
    h2h_service: H2HService = Depends(get_h2h_service),
    historical_matches: list = Depends(get_historical_matches),
):
    """
    Generate betting tickets for a specific date.
    """
    # 1. Load & Analyze
    upcoming_matches = fixture_service.load_upcoming_fixtures(target_date=date)
    if not upcoming_matches:
        return []
        
    analyses = prediction_service.analyze_matches_for_date(
        matches=upcoming_matches,
        historical_matches=historical_matches,
        comprehensive_service=comprehensive_service,
        dixon_coles_model=dixon_coles,
        match_stats_service=match_stats_service,
        h2h_service=h2h_service
    )
    
    # 2. Generate Tickets (via Service)
    tickets = ticket_service.generate_tickets(
        predictions=analyses,
        min_odds=min_odds,
        max_odds=max_odds
    )
    
    return tickets


@router.get("/tickets/weekly", response_model=WeeklyTicket)
async def get_weekly_tickets(
    start_date: Optional[str] = None,
    # Injecting services...
    prediction_service: PredictionService = Depends(get_prediction_service),
    fixture_service: FixtureService = Depends(get_fixture_service),
    ticket_service: TicketService = Depends(get_ticket_service),
    # ... analysis deps
    comprehensive_service: ComprehensiveAnalysisService = Depends(get_comprehensive_service),
    dixon_coles: DixonColesModel = Depends(get_dixon_coles),
    match_stats_service: MatchStatsService = Depends(get_match_stats_service),
    h2h_service: H2HService = Depends(get_h2h_service),
    historical_matches: list = Depends(get_historical_matches),
):
    """
    Generate tickets for the upcoming week.
    """
    start_date_str = start_date or date.today().isoformat()
    
    # 1. Load ALL upcoming fixtures (no date filter initially)
    # Note: FixtureService.load_upcoming_fixtures currently filters by date if provided.
    # We might want to load all and filter in service, or iterate days.
    # For now, let's load all via a small tweak or just calling load without date if supported.
    # (FixtureService.load_upcoming_fixtures supports optional date, so no date = all)
    
    all_upcoming = fixture_service.load_upcoming_fixtures(target_date=None)
    
    # Filter for next 7 days from start_date
    start_dt = datetime.strptime(start_date_str, "%Y-%m-%d").date()
    valid_matches = []
    
    for m in all_upcoming:
        m_date = datetime.strptime(m["match_date"], "%Y-%m-%d").date()
        days_diff = (m_date - start_dt).days
        if 0 <= days_diff <= 7:
            valid_matches.append(m)
            
    if not valid_matches:
        return WeeklyTicket(
            generated_at=datetime.now().isoformat(),
            start_date=start_date_str,
            tickets=[]
        )
        
    # 2. Analyze
    analyses = prediction_service.analyze_matches_for_date(
        matches=valid_matches,
        historical_matches=historical_matches,
        comprehensive_service=comprehensive_service,
        dixon_coles_model=dixon_coles,
        match_stats_service=match_stats_service,
        h2h_service=h2h_service
    )
    
    # 3. Generate Tickets
    tickets = ticket_service.generate_tickets(analyses)
    
    return WeeklyTicket(
        generated_at=datetime.now().isoformat(),
        start_date=start_date_str,
        tickets=tickets
    )
