"""
Confidence Evaluator.

Evaluates confidence levels based on source agreement and data quality.

Single Responsibility: Calculate confidence metrics.
"""
from dataclasses import dataclass
from typing import List, Dict, Any
from enum import Enum

from src.domain.aggregation.models import ConfidenceLevel
from src.utils.logger import get_logger

logger = get_logger("ConfidenceEvaluator")


@dataclass
class ConfidenceFactors:
    """Breakdown of confidence calculation factors."""
    source_agreement: float  # 0-1, how much sources agree
    data_quality: float      # 0-1, based on sample sizes
    h2h_reliability: float   # 0-1, H2H data quality
    model_stability: float   # 0-1, Poisson/MC agreement
    
    @property
    def overall_score(self) -> float:
        """Weighted combination of factors."""
        return (
            self.source_agreement * 0.40 +
            self.data_quality * 0.25 +
            self.h2h_reliability * 0.15 +
            self.model_stability * 0.20
        )
    
    def to_dict(self) -> Dict[str, Any]:
        return {
            "source_agreement": round(self.source_agreement, 2),
            "data_quality": round(self.data_quality, 2),
            "h2h_reliability": round(self.h2h_reliability, 2),
            "model_stability": round(self.model_stability, 2),
            "overall": round(self.overall_score, 2),
        }


class ConfidenceEvaluator:
    """
    Evaluates confidence based on multiple factors.
    
    Factors:
    1. Source Agreement: Do Poisson, MC, Form, H2H agree?
    2. Data Quality: Do we have enough sample data?
    3. H2H Reliability: Do we have good H2H history?
    4. Model Stability: Do Poisson and MC produce similar results?
    """
    
    def evaluate(
        self,
        probabilities: List[float],
        home_sample_size: int,
        away_sample_size: int,
        h2h_matches: int,
        poisson_prob: float,
        mc_prob: float,
    ) -> ConfidenceFactors:
        """
        Evaluate confidence factors for a market.
        
        Args:
            probabilities: All source probabilities for the market
            home_sample_size: Number of home team matches used
            away_sample_size: Number of away team matches used
            h2h_matches: Number of H2H matches available
            poisson_prob: Poisson model probability
            mc_prob: Monte Carlo probability
            
        Returns:
            ConfidenceFactors with breakdown
        """
        source_agreement = self._calculate_source_agreement(probabilities)
        data_quality = self._calculate_data_quality(home_sample_size, away_sample_size)
        h2h_reliability = self._calculate_h2h_reliability(h2h_matches)
        model_stability = self._calculate_model_stability(poisson_prob, mc_prob)
        
        return ConfidenceFactors(
            source_agreement=source_agreement,
            data_quality=data_quality,
            h2h_reliability=h2h_reliability,
            model_stability=model_stability,
        )
    
    def get_confidence_level(self, factors: ConfidenceFactors) -> ConfidenceLevel:
        """Convert factors to a confidence level."""
        score = factors.overall_score
        
        if score >= 0.70:
            return ConfidenceLevel.HIGH
        elif score >= 0.45:
            return ConfidenceLevel.MEDIUM
        else:
            return ConfidenceLevel.LOW
    
    def get_confidence_index(self, factors: ConfidenceFactors) -> int:
        """Convert factors to 0-100 index."""
        return int(factors.overall_score * 100)
    
    def _calculate_source_agreement(self, probabilities: List[float]) -> float:
        """
        Calculate agreement score (0-1).
        
        1.0 = all sources agree perfectly
        0.0 = maximum disagreement (spread of 1.0)
        """
        if len(probabilities) < 2:
            return 0.5
        
        spread = max(probabilities) - min(probabilities)
        # Convert spread (0-1) to agreement (1-0)
        agreement = max(0.0, 1.0 - spread)
        
        return agreement
    
    def _calculate_data_quality(
        self,
        home_sample: int,
        away_sample: int,
    ) -> float:
        """
        Calculate data quality score (0-1).
        
        1.0 = 5+ matches for both teams
        0.5 = 3 matches average
        0.0 = no data
        """
        avg_sample = (home_sample + away_sample) / 2
        
        if avg_sample >= 5:
            return 1.0
        elif avg_sample >= 3:
            return 0.75
        elif avg_sample >= 1:
            return 0.50
        else:
            return 0.25
    
    def _calculate_h2h_reliability(self, h2h_matches: int) -> float:
        """
        Calculate H2H reliability score (0-1).
        
        1.0 = 5+ H2H matches
        0.0 = no H2H data
        """
        if h2h_matches >= 5:
            return 1.0
        elif h2h_matches >= 3:
            return 0.8
        elif h2h_matches >= 2:
            return 0.6
        elif h2h_matches >= 1:
            return 0.4
        else:
            return 0.0
    
    def _calculate_model_stability(
        self,
        poisson: float,
        mc: float,
    ) -> float:
        """
        Calculate Poisson/MC agreement (0-1).
        
        MC adjusts Poisson, so they should be close.
        Large divergence indicates high uncertainty.
        """
        diff = abs(poisson - mc)
        
        # Convert difference to stability
        if diff < 0.05:
            return 1.0
        elif diff < 0.10:
            return 0.8
        elif diff < 0.15:
            return 0.6
        elif diff < 0.20:
            return 0.4
        else:
            return 0.2
