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
from src.domain.services.gemini_service import GeminiService

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
    gemini_service: Optional[GeminiService] = None
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
        cls.backtest_service = BacktestService()
        cls.fixture_service = FixtureService()
        cls.gemini_service = GeminiService()
        cls.ticket_service = TicketService(gemini_service=cls.gemini_service)
        
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

# ComprehensiveAnalysisService removed - merged into PredictionService

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

def get_gemini_service() -> GeminiService:
    if not ServiceContainer.gemini_service:
        return GeminiService()
    return ServiceContainer.gemini_service

def get_dixon_coles() -> Optional[DixonColesModel]:
    return ServiceContainer.dixon_coles

def get_historical_matches() -> list:
    return ServiceContainer.historical_matches

def get_backtest_service() -> BacktestService:
    if not ServiceContainer.backtest_service:
        return BacktestService()
    return ServiceContainer.backtest_service


# ============== Clean Architecture Dependencies ==============

# Lazy-loaded instances (initialized once, thread-safe via GIL)
_upcoming_repository = None
_historical_repository = None
_match_analyzer = None
_analyze_use_case = None


def get_analyze_matches_use_case():
    """
    Factory for AnalyzeMatchesUseCase with all dependencies injected.
    
    Uses proper dependency injection - no global mutable state in router.
    """
    global _upcoming_repository, _historical_repository, _match_analyzer, _analyze_use_case
    
    if _analyze_use_case is not None:
        return _analyze_use_case
    
    # Import here to avoid circular imports
    from src.infrastructure.repositories.match_repository import (
        UpcomingMatchRepository,
        HistoricalMatchRepository,
        LEAGUE_NAMES,
    )
    from src.application.use_cases.analyze_matches import (
        AnalyzeMatchesUseCase,
        MatchAnalyzer,
    )
    from src.domain.services.calculators.team_form_calculator import TeamFormCalculator
    from src.domain.services.calculators.h2h_stats_calculator import H2HStatsCalculator
    from src.domain.services.calculators.poisson_goal_calculator import PoissonGoalCalculator
    from src.domain.services.calculators.monte_carlo_uncertainty_adjuster import MonteCarloUncertaintyAdjuster
    from src.domain.services.calculators.match_confidence_calculator import MatchConfidenceCalculator
    
    # Create repositories
    _upcoming_repository = UpcomingMatchRepository()
    _historical_repository = HistoricalMatchRepository()
    _historical_repository.set_matches(ServiceContainer.historical_matches)
    
    # Create calculators
    form_calc = TeamFormCalculator()
    h2h_calc = H2HStatsCalculator()
    poisson_calc = PoissonGoalCalculator()
    mc_adjuster = MonteCarloUncertaintyAdjuster()
    confidence_calc = MatchConfidenceCalculator()
    
    # Create match analyzer
    _match_analyzer = MatchAnalyzer(
        form_calculator=form_calc,
        h2h_calculator=h2h_calc,
        poisson_calculator=poisson_calc,
        monte_carlo_adjuster=mc_adjuster,
        confidence_calculator=confidence_calc,
        league_names=LEAGUE_NAMES,
    )
    
    # Create AI analyzer adapter (real Gemini)
    ai_analyzer = _create_ai_analyzer()
    
    # Create use case
    _analyze_use_case = AnalyzeMatchesUseCase(
        upcoming_repository=_upcoming_repository,
        historical_repository=_historical_repository,
        match_analyzer=_match_analyzer,
        ai_analyzer=ai_analyzer,
    )
    
    logger.info("Initialized AnalyzeMatchesUseCase with dependencies")
    return _analyze_use_case


def get_match_analyzer():
    """Get the initialized MatchAnalyzer instance."""
    # Ensure initialized
    if _match_analyzer is None:
        get_analyze_matches_use_case()
    return _match_analyzer


def _create_ai_analyzer():
    """Create AI analyzer adapter for use case."""
    from src.domain.services.ai_analysis_service import AIAnalysisService
    
    class AIAnalyzerAdapter:
        """Adapter between AIAnalysisService and use case interface."""
        
        def __init__(self):
            self._service = AIAnalysisService()
        
        def enrich_batch(self, analyses, date: str = None, refresh: bool = False):
            """Enrich analyses with AI predictions.
            
            Args:
                analyses: List of SingleMatchAnalysis objects
                date: Date string for caching (YYYY-MM-DD)
                refresh: Force refresh from AI (bypass cache)
            """
            from collections import defaultdict
            
            # Group by league
            by_league = defaultdict(list)
            for analysis in analyses:
                by_league[analysis.league].append(analysis)
            
            # Process each league
            for league, league_analyses in by_league.items():
                # Prepare data for AI
                match_data = []
                for a in league_analyses:
                    match_data.append({
                        "match_id": a.match_id,
                        "home_team": a.home_team,
                        "away_team": a.away_team,
                        "date": a.date,
                        "home_last_5": a.home_last_5.to_dict(),
                        "away_last_5": a.away_last_5.to_dict(),
                        "h2h_last_5": a.h2h_stats.to_dict(),
                        "poisson": a.poisson.to_dict(),
                        "overall_confidence": a.overall_confidence,
                    })
                
                try:
                    # Call AI service with caching support
                    ai_results = self._service.analyze_matches_batch(
                        matches=match_data,
                        league=league,
                        date=date,
                        refresh=refresh,
                    )
                    
                    # Merge results back
                    from src.application.use_cases.analyze_matches import AIAnalysis
                    
                    for analysis in league_analyses:
                        if analysis.match_id in ai_results:
                            ai_data = ai_results[analysis.match_id]
                            analysis.ai_analysis = AIAnalysis(
                                best_prediction=ai_data.best_prediction,
                                reason=ai_data.reason,
                                short_analysis=ai_data.short_analysis,
                                confidence_level=ai_data.confidence_level,
                            )
                except Exception as e:
                    logger.error(f"AI analysis failed for league {league}: {e}")
            
            return analyses
    
    return AIAnalyzerAdapter()
