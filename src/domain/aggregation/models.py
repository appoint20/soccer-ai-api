"""
Data models for probability aggregation.

These models are:
- Immutable (frozen=True)
- Type-hinted
- JSON-serializable
- Single responsibility: data representation
"""
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Any
from enum import Enum


class ConfidenceLevel(str, Enum):
    """Confidence level based on source agreement."""
    HIGH = "HIGH"
    MEDIUM = "MEDIUM"
    LOW = "LOW"





class MarketType(str, Enum):
    """Supported betting markets."""
    OVER_25 = "over_25"
    BTTS = "btts"
    GOALS_2_3 = "goals_2_3"
    HOME_WIN = "home_win"
    AWAY_WIN = "away_win"
    DRAW = "draw"


@dataclass(frozen=True)
class MarketSourceBreakdown:
    """
    Individual source contribution to a market probability.
    
    Used for explainability - shows where the probability came from.
    """
    source_name: str
    probability: float
    weight: float
    weighted_contribution: float  # probability * weight
    
    def to_dict(self) -> Dict[str, Any]:
        return {
            "source": self.source_name,
            "probability": round(self.probability, 3),
            "weight": round(self.weight, 2),
            "contribution": round(self.weighted_contribution, 3),
        }


@dataclass(frozen=True)
class AggregatedMarket:
    """
    Aggregated probability for a single betting market.
    
    Contains:
    - final_probability: Weighted combination of all sources
    - confidence: HIGH/MEDIUM/LOW based on source agreement
    - verdict: LIKELY/POSSIBLE/UNLIKELY based on probability
    - sources: Raw breakdown for explainability
    """
@dataclass(frozen=True)
class AggregatedMarket:
    """
    Aggregated probability for a single betting market.
    
    Contains:
    - final_probability: Weighted combination of all sources
    - confidence: HIGH/MEDIUM/LOW based on source agreement
    - qualified: Boolean flag indicating if market meets strict entry criteria
    - sources: Raw breakdown for explainability
    """
    market: MarketType
    final_probability: float
    confidence: ConfidenceLevel
    qualified: bool
    sources: List[MarketSourceBreakdown]
    source_variance: float = 0.0  # How much sources disagree
    
    def to_dict(self) -> Dict[str, Any]:
        return {
            "market": self.market.value,
            "probability": round(self.final_probability, 3),
            "probability_pct": f"{round(self.final_probability * 100)}%",
            "confidence": self.confidence.value,
            "qualified": self.qualified,
            "sources": [s.to_dict() for s in self.sources],
            "source_variance": round(self.source_variance, 3),
        }


@dataclass(frozen=True)
class AggregatedMarketsResult:
    """
    Complete aggregation result for all markets.
    
    Chart-ready data structure for frontend consumption.
    """
    over_25: AggregatedMarket
    btts: AggregatedMarket
    goals_2_3: AggregatedMarket
    home_win: AggregatedMarket
    away_win: AggregatedMarket
    draw: AggregatedMarket
    overall_confidence_index: int  # 0-100
    
    def to_dict(self) -> Dict[str, Any]:
        return {
            "over_25": self.over_25.to_dict(),
            "btts": self.btts.to_dict(),
            "goals_2_3": self.goals_2_3.to_dict(),
            "home_win": self.home_win.to_dict(),
            "away_win": self.away_win.to_dict(),
            "draw": self.draw.to_dict(),
            "confidence_index": self.overall_confidence_index,
        }
    
    def get_radar_chart_data(self) -> Dict[str, float]:
        """Pre-computed data for radar chart visualization."""
        return {
            "over_25": round(self.over_25.final_probability, 2),
            "btts": round(self.btts.final_probability, 2),
            "goals_2_3": round(self.goals_2_3.final_probability, 2),
            "home_win": round(self.home_win.final_probability, 2),
            "draw": round(self.draw.final_probability, 2),
            "away_win": round(self.away_win.final_probability, 2),
        }
    
    def get_best_markets(self, min_probability: float = 0.55) -> List[AggregatedMarket]:
        """Get markets with probability above threshold."""
        all_markets = [
            self.over_25, self.btts, self.goals_2_3,
            self.home_win, self.away_win, self.draw,
        ]
        return [
            m for m in all_markets
            if m.final_probability >= min_probability and m.confidence != ConfidenceLevel.LOW
        ]


@dataclass(frozen=True)
class SourceProbabilities:
    """
    Input container for all probability sources.
    
    This standardizes input to the aggregator.
    """
    # Poisson model probabilities
    poisson_over_25: float
    poisson_btts: float
    poisson_goals_2_3: float
    poisson_home_win: float
    poisson_away_win: float
    poisson_draw: float
    
    # Monte Carlo adjusted probabilities
    mc_over_25: float
    mc_btts: float
    mc_goals_2_3: float
    mc_home_win: float
    mc_away_win: float
    mc_draw: float
    
    # Form stats (team-based rates)
    home_form_over_25: float
    home_form_btts: float
    home_form_goals_2_3: float
    away_form_over_25: float
    away_form_btts: float
    away_form_goals_2_3: float
    
    # H2H stats
    h2h_over_25: float
    h2h_btts: float
    h2h_goals_2_3: float
    h2h_home_win: float
    h2h_away_win: float
    h2h_draw: float
    h2h_reliability: float = 0.0  # 0-1, used to weight H2H
