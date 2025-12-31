"""
Predictions router for match analysis and ticket generation.
"""
from datetime import datetime
from typing import Optional, List
import uuid

from fastapi import APIRouter, HTTPException, Query

from src.api.schemas import (
    AnalyzeMatchesResponse,
    ComprehensiveAnalyzeResponse,
    ComprehensiveMatchAnalysis,
    GenerateTicketsResponse,
    MatchAnalysis,
    MatchTeamStats,
    MatchOdds,
    MatchAverage,
    MatchH2H,
    MLModelPrediction,
    PoissonDistribution,
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
    ModelAgreementLevel,
    BacktestReportResponse,
    LeagueQualificationStats,
    MarketAccuracyReport,
    WeeklyTicketsResponse,
    WeeklyTicket,
)
from src.domain.services.prediction_service import PredictionService
from src.domain.services.comprehensive_analysis_service import ComprehensiveAnalysisService
from src.domain.services.backtest_service import BacktestService
from src.domain.services.match_stats_service import MatchStatsService
from src.domain.services.team_stats_service import TeamStatsService
from src.domain.services.h2h_service import H2HService
from src.domain.services.backtest_report_service import BacktestReportService
from src.domain.services.weekly_ticket_service import WeeklyTicketService
from src.statistics.dixon_coles_model import DixonColesModel
from src.data.loaders.csv_loader import CSVLoader
from src.data.storage.json_storage import JSONStorage
from src.utils.logger import get_logger

router = APIRouter()
logger = get_logger("PredictionsRouter")

# Global services
_prediction_service: Optional[PredictionService] = None
_comprehensive_service: Optional[ComprehensiveAnalysisService] = None
_backtest_service: Optional[BacktestService] = None
_match_stats_service: Optional[MatchStatsService] = None
_team_stats_service: Optional[TeamStatsService] = None
_h2h_service: Optional[H2HService] = None
_dixon_coles: Optional[DixonColesModel] = None
_historical_matches: List = []


def load_prediction_service():
    """Load prediction service and historical data."""
    global _prediction_service, _comprehensive_service, _match_stats_service
    global _team_stats_service, _h2h_service, _dixon_coles, _historical_matches
    
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
    
    # Initialize match stats service
    _match_stats_service = MatchStatsService()
    
    # Initialize team stats and H2H services
    _team_stats_service = TeamStatsService()
    _h2h_service = H2HService()
    
    # Initialize Dixon-Coles model
    try:
        _dixon_coles = DixonColesModel(xi=0.01, rho=-0.10)
        _dixon_coles.fit(_historical_matches)
        logger.info("Initialized Dixon-Coles model")
    except Exception as e:
        logger.warning(f"Could not initialize Dixon-Coles: {e}")
    
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
            home_team = match.get("home_team", "")
            away_team = match.get("away_team", "")
            league = match.get("league", "")
            
            # ML Prediction
            prediction = _prediction_service.predict_match(match, _historical_matches)
            over25 = prediction.get("over25", {})
            btts = prediction.get("btts", {})
            result = prediction.get("result", {})
            
            # Odds (from fixture data if available)
            odds_data = None
            if match.get("home_odds") or match.get("B365H"):
                odds_data = MatchOdds(
                    home=float(match.get("home_odds") or match.get("B365H") or 0),
                    draw=float(match.get("draw_odds") or match.get("B365D") or 0),
                    away=float(match.get("away_odds") or match.get("B365A") or 0),
                )
            
            # Team Averages
            average_data = None
            if _team_stats_service:
                home_stats = _team_stats_service.calculate_team_stats(
                    home_team, _historical_matches, league=league
                )
                away_stats = _team_stats_service.calculate_team_stats(
                    away_team, _historical_matches, league=league
                )
                
                home_overall = home_stats.get("overall", {})
                away_overall = away_stats.get("overall", {})
                
                average_data = MatchAverage(
                    home_goal_avg=round(home_overall.get("goals_scored_avg", 0), 2),
                    away_goal_avg=round(away_overall.get("goals_scored_avg", 0), 2),
                    home_win_rate=round(home_overall.get("win_rate", 0), 2),
                    away_win_rate=round(away_overall.get("win_rate", 0), 2),
                    home_conceded_avg=round(home_overall.get("goals_conceded_avg", 0), 2),
                    away_conceded_avg=round(away_overall.get("goals_conceded_avg", 0), 2),
                )
            
            # H2H
            h2h_data = None
            if _h2h_service:
                h2h_stats = _h2h_service.get_h2h_stats(home_team, away_team, _historical_matches)
                h2h_data = MatchH2H(
                    total_matches=h2h_stats.get("total_meetings", 0),
                    home_wins=h2h_stats.get("overall_record", {}).get("home_wins", 0),
                    draws=h2h_stats.get("overall_record", {}).get("draws", 0),
                    away_wins=h2h_stats.get("overall_record", {}).get("away_wins", 0),
                    avg_goals=round(h2h_stats.get("goal_statistics", {}).get("avg_total_goals", 0), 2),
                    btts_rate=round(h2h_stats.get("goal_statistics", {}).get("btts_rate", 0), 2),
                    over25_rate=round(h2h_stats.get("goal_statistics", {}).get("over25_rate", 0), 2),
                )
            
            # ML Model structured output
            ml_model_data = MLModelPrediction(
                prediction=result.get("prediction", "D"),
                confidence=max(result.get("probabilities", {}).values()) if result.get("probabilities") else 0.33,
                over25={"prediction": over25.get("prediction", "NO"), "confidence": over25.get("probability", 0.5)},
                btts={"prediction": btts.get("prediction", "NO"), "confidence": btts.get("probability", 0.5)},
            )
            
            # Poisson Distribution (Dixon-Coles)
            poisson_data = None
            if _dixon_coles:
                try:
                    home_xg, away_xg = _dixon_coles.get_expected_goals(home_team, away_team)
                    dc_probs = _dixon_coles.predict_1x2(home_team, away_team)
                    dc_o25 = _dixon_coles.predict_over25_prob(home_team, away_team)
                    dc_btts = _dixon_coles.predict_btts_prob(home_team, away_team)
                    
                    poisson_data = PoissonDistribution(
                        home_win=round(dc_probs.get("home_win", 0), 4),
                        draw=round(dc_probs.get("draw", 0), 4),
                        away_win=round(dc_probs.get("away_win", 0), 4),
                        over25=round(dc_o25, 4),
                        btts=round(dc_btts, 4),
                        expected_home_goals=round(home_xg, 2),
                        expected_away_goals=round(away_xg, 2),
                    )
                except Exception:
                    pass
            
            # Team Stats (BTTS/Over25 qualification)
            team_stats_data = None
            if _match_stats_service:
                stats = _match_stats_service.calculate_match_stats(
                    home_team, away_team, _historical_matches, league=league
                )
                team_stats_data = MatchTeamStats(
                    btts=stats["btts"],
                    over25=stats["over25"],
                    qualification=stats["qualification"],
                )
            
            analysis = MatchAnalysis(
                match_id=prediction.get("match_id"),
                home_team=home_team,
                away_team=away_team,
                date=str(match.get("match_date", ""))[:10],
                time=match.get("time"),
                league=league,
                odds=odds_data,
                average=average_data,
                h2h=h2h_data,
                ml_model=ml_model_data,
                poisson_distribution=poisson_data,
                team_stats=team_stats_data,
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
        model_agreement={
            market: {
                level: ModelAgreementLevel(**data)
                for level, data in levels.items()
            }
            for market, levels in results["model_agreement"].items()
        },
        league_accuracy=[LeagueAccuracy(**la) for la in results["league_accuracy"]],
        weekly_breakdown=results["weekly_breakdown"],
        generated_at=datetime.now().isoformat(),
    )


@router.get("/backtest/report", response_model=BacktestReportResponse)
async def get_backtest_report(
    weeks: int = Query(default=15, ge=1, le=52, description="Weeks to backtest"),
):
    """
    Get detailed backtest report with qualification stats.
    
    Returns:
    - Total qualified/not-qualified matches with percentages
    - Per-league breakdown of qualification and accuracy
    - Market accuracy with qualified vs not-qualified comparison
    """
    global _historical_matches
    
    if not _historical_matches:
        load_prediction_service()
    
    if not _historical_matches:
        raise HTTPException(status_code=500, detail="No historical matches loaded")
    
    # Generate report
    service = BacktestReportService()
    
    try:
        report = service.generate_report(_historical_matches, weeks=weeks)
    except Exception as e:
        logger.error(f"Backtest report failed: {e}")
        raise HTTPException(status_code=500, detail=f"Report generation failed: {str(e)}")
    
    return BacktestReportResponse(
        test_period=report["test_period"],
        total_matches=report["total_matches"],
        qualified_matches=report["qualified_matches"],
        qualified_pct=report["qualified_pct"],
        not_qualified_matches=report["not_qualified_matches"],
        not_qualified_pct=report["not_qualified_pct"],
        market_accuracy={
            k: MarketAccuracyReport(**v) for k, v in report["market_accuracy"].items()
        },
        league_stats=[LeagueQualificationStats(**ls) for ls in report["league_stats"]],
        generated_at=report["generated_at"],
    )


@router.get("/tickets/weekly", response_model=WeeklyTicketsResponse)
async def get_weekly_tickets(
    week_start: str = Query(..., description="Week start date (YYYY-MM-DD)", pattern=r"^\d{4}-\d{2}-\d{2}$"),
):
    """
    Generate 5 weekly betting tickets.
    
    Rules:
    - 2 mixed tickets (can include 1 win/draw each + goals markets)
    - 3 goals-only tickets (only over25/btts)
    - Max 2 games from same league per ticket
    - Min odds: 1.76 for over25/btts, 2.0 for wins
    """
    global _historical_matches
    
    if not _historical_matches:
        load_prediction_service()
    
    if not _historical_matches:
        raise HTTPException(status_code=500, detail="No historical matches loaded")
    
    # Generate tickets
    service = WeeklyTicketService()
    
    try:
        tickets = service.generate_weekly_tickets(_historical_matches, week_start)
    except Exception as e:
        logger.error(f"Ticket generation failed: {e}")
        raise HTTPException(status_code=500, detail=f"Ticket generation failed: {str(e)}")
    
    return WeeklyTicketsResponse(
        week_start=tickets["week_start"],
        week_end=tickets["week_end"],
        mixed_tickets=[WeeklyTicket(**t) for t in tickets["mixed_tickets"]],
        goals_only_tickets=[WeeklyTicket(**t) for t in tickets["goals_only_tickets"]],
        total_tickets=tickets["total_tickets"],
        generated_at=tickets["generated_at"],
    )
