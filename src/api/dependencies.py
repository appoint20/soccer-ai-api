from typing import Optional
from src.domain.services.prediction_service import PredictionService
from src.domain.services.comprehensive_analysis_service import ComprehensiveAnalysisService
from src.domain.services.backtest_service import BacktestService
from src.domain.services.match_stats_service import MatchStatsService
from src.domain.services.team_stats_service import TeamStatsService
from src.domain.services.h2h_service import H2HService
from src.domain.services.model_retraining_service import ModelRetrainingService
from src.statistics.dixon_coles_model import DixonColesModel
from src.data.storage.json_storage import JSONStorage
from src.utils.logger import get_logger

logger = get_logger("ServiceContainer")

from src.domain.services.fixture_service import FixtureService
from src.domain.services.ticket_service import TicketService

class ServiceContainer:
    """Dependency injection container for services."""
    prediction_service: Optional[PredictionService] = None
    comprehensive_service: Optional[ComprehensiveAnalysisService] = None
    backtest_service: Optional[BacktestService] = None
    match_stats_service: Optional[MatchStatsService] = None
    team_stats_service: Optional[TeamStatsService] = None
    h2h_service: Optional[H2HService] = None
    retraining_service: Optional[ModelRetrainingService] = None
    fixture_service: Optional[FixtureService] = None
    ticket_service: Optional[TicketService] = None
    dixon_coles: Optional[DixonColesModel] = None
    historical_matches: list = []

    @classmethod
    def init_services(cls):
        """Initialize all services."""
        logger.info("Initializing services...")
        
        # Load historical matches
        storage = JSONStorage()
        matches_file = "data/processed/matches.json"
        
        try:
            cls.historical_matches = storage.load(matches_file) or []
            logger.info(f"Loaded {len(cls.historical_matches)} historical matches")
        except Exception as e:
            logger.warning(f"Could not load historical matches: {e}")
            cls.historical_matches = []
        
        # Initialize prediction service
        cls.prediction_service = PredictionService()
        try:
            cls.prediction_service.load_models()
            logger.info("Loaded trained models")
        except Exception as e:
            logger.warning(f"Could not load models (will use fallback): {e}")
        
        # Initialize other services
        cls.match_stats_service = MatchStatsService()
        cls.team_stats_service = TeamStatsService()
        cls.h2h_service = H2HService()
        cls.backtest_service = BacktestService()
        cls.fixture_service = FixtureService()
        cls.ticket_service = TicketService()
        
        # Initialize Retraining Service
        cls.retraining_service = ModelRetrainingService()

        # Initialize Dixon-Coles
        try:
            cls.dixon_coles = DixonColesModel(xi=0.01, rho=-0.10)
            cls.dixon_coles.fit(cls.historical_matches)
            logger.info("Initialized Dixon-Coles model")
        except Exception as e:
            logger.warning(f"Could not initialize Dixon-Coles: {e}")
        
        # Initialize comprehensive analysis
        try:
            cls.comprehensive_service = ComprehensiveAnalysisService()
            cls.comprehensive_service.initialize(cls.historical_matches)
            logger.info("Initialized ComprehensiveAnalysisService")
        except Exception as e:
            logger.warning(f"Failed to init comprehensive service: {e}")

def get_prediction_service() -> PredictionService:
    if not ServiceContainer.prediction_service:
        return PredictionService()
    return ServiceContainer.prediction_service

def get_comprehensive_service() -> ComprehensiveAnalysisService:
    if not ServiceContainer.comprehensive_service:
        raise RuntimeError("ComprehensiveAnalysisService not initialized")
    return ServiceContainer.comprehensive_service

def get_match_stats_service() -> MatchStatsService:
    if not ServiceContainer.match_stats_service:
        return MatchStatsService()
    return ServiceContainer.match_stats_service

def get_team_stats_service() -> TeamStatsService:
    if not ServiceContainer.team_stats_service:
        return TeamStatsService()
    return ServiceContainer.team_stats_service

def get_h2h_service() -> H2HService:
    if not ServiceContainer.h2h_service:
        return H2HService()
    return ServiceContainer.h2h_service

def get_retraining_service() -> ModelRetrainingService:
    if not ServiceContainer.retraining_service:
        return ModelRetrainingService()
    return ServiceContainer.retraining_service

def get_fixture_service() -> FixtureService:
    if not ServiceContainer.fixture_service:
        return FixtureService()
    return ServiceContainer.fixture_service

def get_ticket_service() -> TicketService:
    if not ServiceContainer.ticket_service:
        return TicketService()
    return ServiceContainer.ticket_service

def get_dixon_coles() -> Optional[DixonColesModel]:
    return ServiceContainer.dixon_coles

def get_historical_matches() -> list:
    return ServiceContainer.historical_matches

def get_backtest_service() -> BacktestService:
    if not ServiceContainer.backtest_service:
        return BacktestService()
    return ServiceContainer.backtest_service
