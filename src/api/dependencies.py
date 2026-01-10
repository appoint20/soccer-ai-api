from typing import Optional
from fastapi import Depends
from src.infrastructure.repositories.historical_match_repository import CSVHistoricalMatchRepository, IHistoricalMatchRepository
from src.domain.services.team_name_matcher import TeamNameMatcher
from src.domain.services.prediction_evaluator import PredictionEvaluator
from src.application.use_cases.backtest_predictions import BacktestPredictionsUseCase
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
    
    # Get backtest use case first
    backtest_uc = get_backtest_predictions_use_case()
    
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
    
    # Create calculators (inject TeamNameMatcher for fuzzy matching)
    team_matcher = TeamNameMatcher()
    form_calc = TeamFormCalculator(team_matcher=team_matcher)
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

    # Create AI backtest service
    from src.domain.services.ai_prediction_backtest_service import AIPredictionBacktestService
    ai_backtest_service = AIPredictionBacktestService(_historical_repository)

    # Create use case
    _analyze_use_case = AnalyzeMatchesUseCase(
        upcoming_repository=_upcoming_repository,
        historical_repository=_historical_repository,
        match_analyzer=_match_analyzer,
        ai_analyzer=ai_analyzer,
        backtest_service=_create_backtest_adapter(backtest_uc),
        ai_backtest_service=ai_backtest_service,
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
            from datetime import datetime
            
            by_league = defaultdict(list)
            for analysis in analyses:
                league = analysis.league
                by_league[league].append(analysis)
                
            # Enrich each league batch
            enriched_analyses = []
            for league, league_analyses in by_league.items():
                start = datetime.now()
                result_map = self._service.analyze_matches_batch(
                    analyses=league_analyses,
                    league=league,
                    date=date,
                    refresh=refresh
                )
                # result_map is {match_id: AIAnalysis}, we need to attach to analyses
                for analysis in league_analyses:
                    ai_result = result_map.get(analysis.match_id)
                    if ai_result:
                        analysis.ai_analysis = ai_result.to_dict()
                    enriched_analyses.append(analysis)
                logger.debug(f"AI enriched {len(league_analyses)} matches for {league} in {datetime.now() - start}")
                
            # Restore order? The use case loop might rely on order.
            # Map by ID
            enriched_map = {a.match_id: a for a in enriched_analyses}
            
            # Return in original order, maintaining non-enriched if missing
            return [enriched_map.get(a.match_id, a) for a in analyses]

    return AIAnalyzerAdapter()

def _create_backtest_adapter(use_case):
    """Create adapter for BacktestPredictionsUseCase to IBacktestService."""
    from src.application.use_cases.backtest_predictions import BacktestRequest
    from datetime import datetime
    
    class BacktestServiceAdapter:
        def __init__(self, uc):
            self._uc = uc
            self.logger = logger
            
        def calculate_stats(self, analyses: list) -> dict:
            if not analyses:
                return {}
            
            # Determine target date from first analysis
            # Format: YYYY-MM-DD
            target_date = analyses[0].date
            
            try:
                # Execute backtest
                response = self._uc.execute(BacktestRequest(
                    analyses=analyses,
                    target_date=target_date
                ))
                
                # Update analyses with match results (Side Effect)
                for analysis in analyses:
                    if analysis.match_id in response.match_results:
                        # Convert BacktestResult object to dict if needed?
                        # SingleMatchAnalysis.backtest_result is Optional[dict]
                        # response.match_results contains BacktestResult objects (Pydantic/Dataclass?)
                        # Use vars() or .dict() or asdict()
                        res = response.match_results[analysis.match_id]
                        # Assuming it has to_dict or is simple object
                        analysis.backtest_result = {
                            "actual_score": res.actual_score,
                            "actual_result": res.actual_result,
                            "predicted_market": res.predicted_market,
                            "was_correct": res.was_correct,
                            "explanation": res.explanation
                        }
                
                # Return stats as dict
                if response.stats:
                    return {
                        "total_predictions": response.stats.total_predictions,
                        "correct_predictions": response.stats.correct_predictions,
                        "incorrect_predictions": response.stats.incorrect_predictions,
                        "accuracy_percentage": response.stats.accuracy_percentage,
                        "by_market": response.stats.by_market
                    }
                return {}
                
            except Exception as e:
                self.logger.error(f"Backtest adapter failed: {e}")
                return {}

    return BacktestServiceAdapter(use_case)


_backtest_use_case = None

def get_backtest_predictions_use_case():
    """Factory for BacktestPredictionsUseCase."""
    global _backtest_use_case, _historical_repository
    
    if _backtest_use_case:
        return _backtest_use_case
        
    from src.application.use_cases.backtest_predictions import BacktestPredictionsUseCase
    from src.domain.services.prediction_evaluator import PredictionEvaluator
    from src.infrastructure.repositories.match_repository import HistoricalMatchRepository
    
    # Ensure repo is initialized
    if _historical_repository is None:
        _historical_repository = HistoricalMatchRepository()
        _historical_repository.set_matches(ServiceContainer.historical_matches)
        
    evaluator = PredictionEvaluator()
    
    _backtest_use_case = BacktestPredictionsUseCase(
        historical_repo=_historical_repository,
        prediction_evaluator=evaluator
    )
    return _backtest_use_case
