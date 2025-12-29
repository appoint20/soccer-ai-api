"""
Predictions router for match analysis and ticket generation.
"""
from datetime import datetime, date
from typing import Optional, List
import uuid

from fastapi import APIRouter, HTTPException, Query

from src.api.schemas import (
    AnalyzeMatchesResponse,
    ComprehensiveAnalyzeResponse,
    ComprehensiveMatchAnalysis,
    GenerateTicketsResponse,
    MatchAnalysis,
    Over25Prediction,
    BTTSPrediction,
    ResultPrediction,
    ResultProbabilities,
    Ticket,
    TicketSelection,
    BacktestResponse,
    BacktestSummary,
    MarketAccuracy,
    LeagueAccuracy,
)
from src.domain.services.prediction_service import PredictionService
from src.domain.services.feature_engineering_service import FeatureEngineeringService
from src.domain.services.comprehensive_analysis_service import ComprehensiveAnalysisService
from src.domain.services.backtest_service import BacktestService
from src.data.loaders.csv_loader import CSVLoader
from src.data.storage.json_storage import JSONStorage
from src.utils.logger import get_logger

router = APIRouter()
logger = get_logger("PredictionsRouter")

# Global services
_prediction_service: Optional[PredictionService] = None
_comprehensive_service: Optional[ComprehensiveAnalysisService] = None
_backtest_service: Optional[BacktestService] = None
_historical_matches: List = []


def load_prediction_service():
    """Load prediction service and historical data."""
    global _prediction_service, _comprehensive_service, _historical_matches
    
    # Load historical matches
    storage = JSONStorage()
    matches_file = "data/processed/matches.json"
    
    try:
        _historical_matches = storage.load(matches_file) or []
        logger.info(f"Loaded {len(_historical_matches)} historical matches")
    except Exception as e:
        logger.warning(f"Could not load historical matches: {e}")
        _historical_matches = []
    
    # Initialize prediction service
    _prediction_service = PredictionService()
    
    # Try to load trained models
    try:
        _prediction_service.load_models()
        logger.info("Loaded trained models")
    except Exception as e:
        logger.warning(f"Could not load models (will use fallback): {e}")
    
    # Initialize comprehensive analysis service
    try:
        _comprehensive_service = ComprehensiveAnalysisService()
        _comprehensive_service.initialize(_historical_matches)
        logger.info("Initialized comprehensive analysis service")
    except Exception as e:
        logger.warning(f"Could not initialize comprehensive service: {e}")


@router.get("/analyze/matches", response_model=AnalyzeMatchesResponse)
async def analyze_matches(
    date: str = Query(..., description="Date in YYYY-MM-DD format", pattern=r"^\d{4}-\d{2}-\d{2}$"),
):
    """
    Analyze all matches for a given date.
    
    Reads upcoming fixtures from CSV and generates predictions for each match.
    
    Parameters:
    - **date**: Date to analyze (YYYY-MM-DD format)
    
    Returns:
    - List of match analyses with predictions for over2.5, BTTS, and result
    """
    global _prediction_service, _historical_matches
    
    if _prediction_service is None:
        load_prediction_service()
    
    # Load upcoming fixtures
    csv_loader = CSVLoader()
    try:
        df = csv_loader.load("data/raw/upcoming/fixtures.csv")
        if df is None or df.empty:
            fixtures = []
        else:
            # Convert DataFrame to list of dicts
            fixtures = df.to_dict('records')
            # Convert match_date field if needed
            for f in fixtures:
                if 'parsed_date' in f:
                    f['match_date'] = f['parsed_date']
                elif 'date' in f:
                    f['match_date'] = f['date']
    except Exception as e:
        logger.error(f"Failed to load fixtures: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to load fixtures: {e}")
    
    # Filter by date
    target_date = date
    matches_for_date = [
        f for f in fixtures
        if str(f.get("match_date", ""))[:10] == target_date
    ]
    
    if not matches_for_date:
        return AnalyzeMatchesResponse(
            date=date,
            total_matches=0,
            matches=[],
            generated_at=datetime.now().isoformat(),
        )
    
    # Analyze each match
    analyses = []
    for match in matches_for_date:
        try:
            prediction = _prediction_service.predict_match(match, _historical_matches)
            
            over25 = prediction.get("over25", {})
            btts = prediction.get("btts", {})
            result = prediction.get("result", {})
            
            analysis = MatchAnalysis(
                match_id=prediction.get("match_id"),
                home_team=match.get("home_team", ""),
                away_team=match.get("away_team", ""),
                date=str(match.get("match_date", ""))[:10],
                time=match.get("time"),
                league=match.get("league", ""),
                over25=Over25Prediction(
                    prediction=over25.get("prediction", "NO"),
                    probability=over25.get("probability", 0.5),
                    confidence=over25.get("confidence", "LOW"),
                ),
                btts=BTTSPrediction(
                    prediction=btts.get("prediction", "NO"),
                    probability=btts.get("probability", 0.5),
                    confidence=btts.get("confidence", "LOW"),
                ),
                result=ResultPrediction(
                    prediction=result.get("prediction", "D"),
                    probabilities=ResultProbabilities(
                        home_win=result.get("probabilities", {}).get("home_win", 0.33),
                        draw=result.get("probabilities", {}).get("draw", 0.34),
                        away_win=result.get("probabilities", {}).get("away_win", 0.33),
                    ),
                    confidence=result.get("confidence", "LOW"),
                ),
            )
            analyses.append(analysis)
            
        except Exception as e:
            logger.error(f"Failed to analyze match {match}: {e}")
            continue
    
    return AnalyzeMatchesResponse(
        date=date,
        total_matches=len(analyses),
        matches=analyses,
        generated_at=datetime.now().isoformat(),
    )


@router.get("/analyze/comprehensive", response_model=ComprehensiveAnalyzeResponse)
async def analyze_comprehensive(
    date: str = Query(..., description="Date in YYYY-MM-DD format", pattern=r"^\d{4}-\d{2}-\d{2}$"),
):
    """
    Comprehensive analysis with all models and statistics.
    
    Provides:
    - Team statistics (form, goals, etc.)
    - H2H statistics
    - ML predictions with reasons
    - Monte Carlo simulations (draw detection)
    - Dixon-Coles Poisson predictions
    - Ensemble combined predictions with confidence
    
    Parameters:
    - **date**: Date to analyze (YYYY-MM-DD format)
    
    Returns:
    - List of comprehensive match analyses
    """
    global _comprehensive_service, _historical_matches
    
    if _comprehensive_service is None:
        load_prediction_service()
    
    if _comprehensive_service is None:
        raise HTTPException(status_code=500, detail="Comprehensive analysis service not available")
    
    # Load upcoming fixtures
    csv_loader = CSVLoader()
    try:
        df = csv_loader.load("data/raw/upcoming/fixtures.csv")
        if df is None or df.empty:
            fixtures = []
        else:
            fixtures = df.to_dict('records')
            for f in fixtures:
                if 'parsed_date' in f:
                    f['match_date'] = f['parsed_date']
                elif 'date' in f:
                    f['match_date'] = f['date']
    except Exception as e:
        logger.error(f"Failed to load fixtures: {e}")
        raise HTTPException(status_code=500, detail=f"Failed to load fixtures: {e}")
    
    # Filter by date
    target_date = date
    matches_for_date = [
        f for f in fixtures
        if str(f.get("match_date", ""))[:10] == target_date
    ]
    
    if not matches_for_date:
        return ComprehensiveAnalyzeResponse(
            date=date,
            total_matches=0,
            matches=[],
            generated_at=datetime.now().isoformat(),
        )
    
    # Analyze each match
    analyses = []
    for match in matches_for_date:
        try:
            analysis = _comprehensive_service.analyze_match(match, _historical_matches)
            analyses.append(ComprehensiveMatchAnalysis(**analysis))
        except Exception as e:
            logger.error(f"Failed comprehensive analysis for {match}: {e}")
            continue
    
    return ComprehensiveAnalyzeResponse(
        date=date,
        total_matches=len(analyses),
        matches=analyses,
        generated_at=datetime.now().isoformat(),
    )


@router.get("/tickets/generate", response_model=GenerateTicketsResponse)
async def generate_tickets(
    date: str = Query(..., description="Date in YYYY-MM-DD format", pattern=r"^\d{4}-\d{2}-\d{2}$"),
    min_confidence: str = Query(default="MEDIUM", description="Minimum confidence (LOW, MEDIUM, HIGH)"),
):
    """
    Generate betting tickets based on match analysis.
    
    Creates tickets with high-confidence selections from the analyzed matches.
    
    Parameters:
    - **date**: Date for ticket generation (YYYY-MM-DD)
    - **min_confidence**: Minimum confidence level for selections
    
    Returns:
    - List of generated tickets with selections
    """
    # First get match analyses
    analyses_response = await analyze_matches(date)
    analyses = analyses_response.matches
    
    if not analyses:
        return GenerateTicketsResponse(
            date=date,
            tickets=[],
            total_tickets=0,
            generated_at=datetime.now().isoformat(),
        )
    
    # Filter by confidence
    confidence_order = {"HIGH": 3, "MEDIUM": 2, "LOW": 1}
    min_conf_val = confidence_order.get(min_confidence.upper(), 2)
    
    # Collect high-confidence selections
    over25_selections = []
    btts_selections = []
    
    for match in analyses:
        match_str = f"{match.home_team} vs {match.away_team}"
        
        # Over 2.5 selections
        if confidence_order.get(match.over25.confidence, 0) >= min_conf_val:
            over25_selections.append(TicketSelection(
                match=match_str,
                league=match.league,
                time=match.time,
                market="over25",
                selection=match.over25.prediction,
                probability=match.over25.probability,
                confidence=match.over25.confidence,
            ))
        
        # BTTS selections
        if confidence_order.get(match.btts.confidence, 0) >= min_conf_val:
            btts_selections.append(TicketSelection(
                match=match_str,
                league=match.league,
                time=match.time,
                market="btts",
                selection=match.btts.prediction,
                probability=match.btts.probability,
                confidence=match.btts.confidence,
            ))
    
    tickets = []
    
    # Create Over 2.5 ticket (if enough selections)
    if len(over25_selections) >= 3:
        # Sort by probability and take top selections
        over25_sorted = sorted(over25_selections, key=lambda x: x.probability, reverse=True)[:5]
        combined_prob = 1.0
        for sel in over25_sorted:
            combined_prob *= sel.probability
        
        tickets.append(Ticket(
            ticket_id=f"O25-{date}-{uuid.uuid4().hex[:6]}",
            ticket_type="accumulator",
            selections=over25_sorted,
            combined_probability=round(combined_prob, 4),
            risk_level="MEDIUM" if len(over25_sorted) <= 4 else "HIGH",
        ))
    
    # Create BTTS ticket
    if len(btts_selections) >= 3:
        btts_sorted = sorted(btts_selections, key=lambda x: x.probability, reverse=True)[:5]
        combined_prob = 1.0
        for sel in btts_sorted:
            combined_prob *= sel.probability
        
        tickets.append(Ticket(
            ticket_id=f"BTTS-{date}-{uuid.uuid4().hex[:6]}",
            ticket_type="accumulator",
            selections=btts_sorted,
            combined_probability=round(combined_prob, 4),
            risk_level="MEDIUM" if len(btts_sorted) <= 4 else "HIGH",
        ))
    
    # Create mixed ticket (best of both)
    all_selections = over25_selections + btts_selections
    if len(all_selections) >= 4:
        mixed_sorted = sorted(all_selections, key=lambda x: x.probability, reverse=True)[:4]
        combined_prob = 1.0
        for sel in mixed_sorted:
            combined_prob *= sel.probability
        
        tickets.append(Ticket(
            ticket_id=f"MIX-{date}-{uuid.uuid4().hex[:6]}",
            ticket_type="mixed",
            selections=mixed_sorted,
            combined_probability=round(combined_prob, 4),
            risk_level="MEDIUM",
        ))
    
    return GenerateTicketsResponse(
        date=date,
        tickets=tickets,
        total_tickets=len(tickets),
        generated_at=datetime.now().isoformat(),
    )


@router.get("/backtest/run", response_model=BacktestResponse)
async def run_backtest(
    weeks: int = Query(default=10, ge=1, le=52, description="Number of weeks to backtest"),
    confidence: float = Query(default=0.55, ge=0.5, le=0.9, description="Minimum confidence threshold"),
    exclude_derbies: bool = Query(default=False, description="Exclude derby matches"),
):
    """
    Run backtesting for model performance evaluation.
    
    Provides:
    - Total matches qualified vs ignored
    - Per league accuracy breakdown
    - Per market (over25, btts, result) accuracy
    - Weekly breakdown
    
    Parameters:
    - **weeks**: Number of weeks to test (1-52)
    - **confidence**: Minimum confidence threshold (0.5-0.9)
    - **exclude_derbies**: Whether to exclude derby matches
    
    Returns:
    - Backtest results with accuracy metrics
    """
    global _backtest_service, _historical_matches
    
    # Load historical if needed
    if not _historical_matches:
        storage = JSONStorage()
        _historical_matches = storage.load("data/processed/matches.json") or []
        logger.info(f"Loaded {len(_historical_matches)} historical matches for backtest")
    
    if not _historical_matches:
        raise HTTPException(status_code=500, detail="No historical data available")
    
    # Initialize backtest service
    _backtest_service = BacktestService(confidence_threshold=confidence)
    
    # Run backtest
    try:
        results = _backtest_service.run_backtest(
            _historical_matches,
            weeks=weeks,
            exclude_derbies=exclude_derbies,
        )
    except Exception as e:
        logger.error(f"Backtest failed: {e}")
        raise HTTPException(status_code=500, detail=f"Backtest failed: {str(e)}")
    
    # Convert to response model
    return BacktestResponse(
        summary=BacktestSummary(**results["summary"]),
        market_accuracy={
            k: MarketAccuracy(**v) for k, v in results["market_accuracy"].items()
        },
        league_accuracy=[LeagueAccuracy(**la) for la in results["league_accuracy"]],
        weekly_breakdown=results["weekly_breakdown"],
        generated_at=datetime.now().isoformat(),
    )
