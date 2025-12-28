"""Prediction entity for match predictions."""
from dataclasses import dataclass, field, asdict
from datetime import datetime
from typing import Optional
import uuid


@dataclass
class ResultProbabilities:
    """
    Probabilities for match result outcomes.
    
    Attributes:
        home_win: Probability of home win (0-1)
        draw: Probability of draw (0-1)
        away_win: Probability of away win (0-1)
    """
    home_win: float = 0.0
    draw: float = 0.0
    away_win: float = 0.0
    
    def to_dict(self) -> dict:
        """Convert to dictionary."""
        return asdict(self)
    
    @classmethod
    def from_dict(cls, data: dict) -> "ResultProbabilities":
        """Create from dictionary."""
        return cls(**data)
    
    @property
    def predicted_result(self) -> str:
        """Get the most likely result."""
        max_prob = max(self.home_win, self.draw, self.away_win)
        if max_prob == self.home_win:
            return "H"
        elif max_prob == self.away_win:
            return "A"
        return "D"
    
    @property
    def max_probability(self) -> float:
        """Get the highest probability."""
        return max(self.home_win, self.draw, self.away_win)


@dataclass
class Prediction:
    """
    Represents a prediction for a match.
    
    Attributes:
        match_id: ID of the match being predicted
        prediction_date: When the prediction was made
        model_version: Version of the model used
        
        Over 2.5 Goals:
        over25_prediction: True if predicting over 2.5 goals
        over25_probability: Probability of over 2.5 goals (0-1)
        over25_confidence: Confidence level ('high', 'medium', 'low')
        
        BTTS:
        btts_prediction: True if predicting both teams to score
        btts_probability: Probability of BTTS (0-1)
        btts_confidence: Confidence level
        
        Match Result:
        result_prediction: Predicted result ('H', 'D', 'A')
        result_probabilities: Probabilities for each outcome
        
        Actual Results:
        actual_over25: Actual over 2.5 result (filled after match)
        actual_btts: Actual BTTS result
        actual_result: Actual match result
    """
    
    match_id: str
    model_version: str = "1.0.0"
    
    # Metadata
    id: str = field(default_factory=lambda: str(uuid.uuid4()))
    prediction_date: datetime = field(default_factory=datetime.now)
    
    # Over 2.5 Goals prediction
    over25_prediction: bool = False
    over25_probability: float = 0.0
    over25_confidence: str = "low"
    
    # BTTS prediction
    btts_prediction: bool = False
    btts_probability: float = 0.0
    btts_confidence: str = "low"
    
    # Match result prediction
    result_prediction: str = "D"
    result_probabilities: ResultProbabilities = field(
        default_factory=ResultProbabilities
    )
    
    # Actual results (populated after match)
    actual_over25: Optional[bool] = None
    actual_btts: Optional[bool] = None
    actual_result: Optional[str] = None
    
    @property
    def is_verified(self) -> bool:
        """Check if prediction has been verified with actual results."""
        return self.actual_result is not None
    
    @property
    def over25_correct(self) -> Optional[bool]:
        """Check if over 2.5 prediction was correct."""
        if self.actual_over25 is None:
            return None
        return self.over25_prediction == self.actual_over25
    
    @property
    def btts_correct(self) -> Optional[bool]:
        """Check if BTTS prediction was correct."""
        if self.actual_btts is None:
            return None
        return self.btts_prediction == self.actual_btts
    
    @property
    def result_correct(self) -> Optional[bool]:
        """Check if result prediction was correct."""
        if self.actual_result is None:
            return None
        return self.result_prediction == self.actual_result
    
    def set_actual_results(
        self,
        over25: bool,
        btts: bool,
        result: str
    ) -> None:
        """Set actual match results after the match."""
        self.actual_over25 = over25
        self.actual_btts = btts
        self.actual_result = result
    
    def to_dict(self) -> dict:
        """Convert to dictionary for JSON serialization."""
        data = asdict(self)
        # Convert datetime to ISO format
        data["prediction_date"] = self.prediction_date.isoformat()
        # Handle nested object
        data["result_probabilities"] = self.result_probabilities.to_dict()
        return data
    
    @classmethod
    def from_dict(cls, data: dict) -> "Prediction":
        """Create Prediction from dictionary."""
        # Parse datetime
        if isinstance(data.get("prediction_date"), str):
            data["prediction_date"] = datetime.fromisoformat(
                data["prediction_date"]
            )
        
        # Parse result probabilities
        if isinstance(data.get("result_probabilities"), dict):
            data["result_probabilities"] = ResultProbabilities.from_dict(
                data["result_probabilities"]
            )
        
        return cls(**data)
    
    @staticmethod
    def calculate_confidence(probability: float) -> str:
        """
        Calculate confidence level from probability.
        
        Args:
            probability: Probability value (0-1)
            
        Returns:
            Confidence level string ('high', 'medium', 'low')
        """
        if probability >= 0.7:
            return "high"
        elif probability >= 0.55:
            return "medium"
        return "low"
    
    def __repr__(self) -> str:
        """String representation."""
        status = "verified" if self.is_verified else "pending"
        return (
            f"Prediction(match={self.match_id}, "
            f"O2.5={self.over25_prediction}@{self.over25_probability:.2f}, "
            f"BTTS={self.btts_prediction}@{self.btts_probability:.2f}, "
            f"result={self.result_prediction}, status={status})"
        )
