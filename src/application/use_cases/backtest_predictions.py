from dataclasses import dataclass
from typing import List, Dict, Optional
from datetime import date
from src.domain.services.prediction_evaluator import PredictionEvaluator
from src.infrastructure.repositories.historical_match_repository import IHistoricalMatchRepository
from src.application.use_cases.analyze_matches import SingleMatchAnalysis
from src.utils.logger import get_logger

logger = get_logger("BacktestPredictionsUseCase")

@dataclass
class BacktestRequest:
    """Request to backtest predictions for a specific date."""
    analyses: List[SingleMatchAnalysis]
    target_date: date

@dataclass
class BacktestResult:
    """Result of backtesting."""
    actual_score: str
    actual_result: str
    predicted_market: str
    was_correct: bool
    explanation: Optional[str]

@dataclass
class BacktestStats:
    """Aggregated backtest statistics."""
    total_predictions: int
    correct_predictions: int
    incorrect_predictions: int
    accuracy_percentage: float
    by_market: Dict[str, Dict[str, int]]

@dataclass
class BacktestResponse:
    """Response containing all backtest results."""
    match_results: Dict[str, BacktestResult]  # match_id -> result
    stats: BacktestStats

class BacktestPredictionsUseCase:
    """
    Use Case: Backtest predictions against actual results.
    
    Responsibilities:
    - Find actual results for past matches
    - Evaluate prediction accuracy
    - Calculate aggregate statistics
    """
    
    def __init__(
        self,
        historical_repo: IHistoricalMatchRepository,
        prediction_evaluator: PredictionEvaluator,
    ):
        self._historical_repo = historical_repo
        self._evaluator = prediction_evaluator
    
    def execute(self, request: BacktestRequest) -> BacktestResponse:
        """Execute backtesting."""
        
        match_results = {}
        stats_tracker = {
            "total": 0,
            "correct": 0,
            "by_market": {}
        }
        
        for analysis in request.analyses:
            # Find actual result
            actual_match = self._historical_repo.find_by_teams_and_date(
                analysis.home_team,
                analysis.away_team,
                request.target_date
            )
            
            if not actual_match or not actual_match.result:
                # logger.warning(f"No actual result found for {analysis.match_id}")
                continue
            
            # Get prediction
            predicted_market = self._extract_prediction(analysis)
            if predicted_market == "Unknown":
                continue
            
            # Evaluate
            evaluation = self._evaluator.evaluate(
                predicted_market,
                actual_match.result
            )
            
            # Store result
            match_results[analysis.match_id] = BacktestResult(
                actual_score=actual_match.result.score,
                actual_result=actual_match.result.outcome,
                predicted_market=predicted_market,
                was_correct=evaluation.was_correct,
                explanation=evaluation.explanation,
            )
            
            # Update stats
            self._update_stats(stats_tracker, predicted_market, evaluation.was_correct)
        
        # Calculate final stats
        stats = self._calculate_stats(stats_tracker)
        
        return BacktestResponse(
            match_results=match_results,
            stats=stats
        )
    
    def _extract_prediction(self, analysis: SingleMatchAnalysis) -> str:
        """Extract predicted market from analysis."""
        if analysis.ai_analysis and analysis.ai_analysis.best_prediction:
            return analysis.ai_analysis.best_prediction
        return "Unknown"
    
    def _update_stats(self, tracker: dict, market: str, was_correct: bool):
        """Update statistics tracker."""
        tracker["total"] += 1
        if was_correct:
            tracker["correct"] += 1
        
        if market not in tracker["by_market"]:
            tracker["by_market"][market] = {"total": 0, "correct": 0}
        
        tracker["by_market"][market]["total"] += 1
        if was_correct:
            tracker["by_market"][market]["correct"] += 1
    
    def _calculate_stats(self, tracker: dict) -> BacktestStats:
        """Calculate final backtest statistics."""
        total = tracker["total"]
        correct = tracker["correct"]
        
        return BacktestStats(
            total_predictions=total,
            correct_predictions=correct,
            incorrect_predictions=total - correct,
            accuracy_percentage=round((correct / total * 100), 1) if total > 0 else 0.0,
            by_market=tracker["by_market"]
        )
