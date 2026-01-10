from dataclasses import dataclass
from typing import Optional
from src.domain.entities.match import MatchResult

@dataclass
class PredictionEvaluation:
    """Result of evaluating a prediction."""
    was_correct: bool
    explanation: Optional[str] = None

class PredictionEvaluator:
    """
    Domain service for evaluating prediction accuracy.
    
    Responsibilities:
    - Parse prediction strings
    - Compare predictions with actual results
    - Generate explanations
    """
    
    def evaluate(
        self,
        prediction: str,
        actual_result: MatchResult
    ) -> PredictionEvaluation:
        """Evaluate if prediction was correct."""
        
        pred_lower = prediction.lower().strip()
        
        # Handle non-predictions
        if pred_lower in ["no bet", "skip", "none", ""]:
            return PredictionEvaluation(
                was_correct=False,
                explanation="No prediction made"
            )
        
        # Evaluate by market type
        if self._is_over_25_prediction(pred_lower):
            return self._evaluate_over_25(actual_result)
        
        if self._is_btts_prediction(pred_lower):
            return self._evaluate_btts(actual_result)
        
        if self._is_result_prediction(pred_lower):
            return self._evaluate_result(pred_lower, actual_result)
        
        return PredictionEvaluation(
            was_correct=False,
            explanation=f"Unknown prediction type: {prediction}"
        )
    
    def _is_over_25_prediction(self, pred: str) -> bool:
        return "over 2.5" in pred or pred == "o2.5"
    
    def _is_btts_prediction(self, pred: str) -> bool:
        return "btts" in pred
    
    def _is_result_prediction(self, pred: str) -> bool:
        return pred in ["home win", "away win", "draw", "home", "away", "1", "2", "x", "h", "a", "d"]
    
    def _evaluate_over_25(self, result: MatchResult) -> PredictionEvaluation:
        """Evaluate Over 2.5 prediction."""
        is_over = result.total_goals > 2.5
        
        if is_over:
            return PredictionEvaluation(was_correct=True)
        
        return PredictionEvaluation(
            was_correct=False,
            explanation=f"Only {result.total_goals} goals scored ({result.score})"
        )
    
    def _evaluate_btts(self, result: MatchResult) -> PredictionEvaluation:
        """Evaluate BTTS prediction."""
        both_scored = result.home_goals > 0 and result.away_goals > 0
        
        if both_scored:
            return PredictionEvaluation(was_correct=True)
        
        if result.home_goals == 0:
            explanation = f"Home team failed to score ({result.score})"
        else:
            explanation = f"Away team failed to score ({result.score})"
        
        return PredictionEvaluation(was_correct=False, explanation=explanation)
    
    def _evaluate_result(self, prediction: str, result: MatchResult) -> PredictionEvaluation:
        """Evaluate match result prediction."""
        # Map prediction to H/D/A
        expected = None
        if prediction in ["home win", "home", "1", "h"]:
            expected = "H"
        elif prediction in ["away win", "away", "2", "a"]:
            expected = "A"
        elif prediction in ["draw", "x", "d"]:
            expected = "D"
        else:
            return PredictionEvaluation(
                was_correct=False,
                explanation=f"Unknown result prediction: {prediction}"
            )
        
        if result.outcome == expected:
            return PredictionEvaluation(was_correct=True)
        
        # Generate explanation
        if result.outcome == "D":
            explanation = f"Match ended in draw ({result.score})"
        elif result.outcome == "H":
            explanation = f"Home team won ({result.score})"
        else:
            explanation = f"Away team won ({result.score})"
        
        return PredictionEvaluation(was_correct=False, explanation=explanation)
