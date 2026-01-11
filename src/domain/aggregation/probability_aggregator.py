"""
Probability Aggregator.

Combines probabilities from multiple sources using weighted aggregation.

Single Responsibility: Aggregate probabilities from disparate sources.
"""
from dataclasses import dataclass
from typing import Dict, List, Tuple
import statistics

from src.domain.aggregation.models import (
    AggregatedMarket,
    AggregatedMarketsResult,
    MarketSourceBreakdown,
    SourceProbabilities,
    MarketType,
    ConfidenceLevel,
    MarketType,
    ConfidenceLevel,
)
from src.utils.logger import get_logger

logger = get_logger("ProbabilityAggregator")


@dataclass
class SourceWeight:
    """Weight configuration for a probability source."""
    name: str
    base_weight: float


class ProbabilityAggregator:
    """
    Aggregates probabilities from multiple sources using weighted combination.
    
    Weights:
    - Poisson: 40% (statistical model, most reliable)
    - Monte Carlo: 25% (uncertainty-adjusted)
    - Home Form: 15% (recent performance)
    - Away Form: 15% (recent performance)
    - H2H: 5% (historical, redistributed if unreliable)
    
    Total: 100%
    """
    
    # Default weights
    WEIGHT_POISSON = 0.40
    WEIGHT_MONTE_CARLO = 0.25
    WEIGHT_HOME_FORM = 0.15
    WEIGHT_AWAY_FORM = 0.15
    WEIGHT_H2H = 0.05
    
    # H2H minimum reliability threshold
    H2H_MIN_RELIABILITY = 0.4  # Need at least 2 matches
    
    def __init__(
        self,
        poisson_weight: float = WEIGHT_POISSON,
        mc_weight: float = WEIGHT_MONTE_CARLO,
        home_form_weight: float = WEIGHT_HOME_FORM,
        away_form_weight: float = WEIGHT_AWAY_FORM,
        h2h_weight: float = WEIGHT_H2H,
    ):
        """Initialize with configurable weights."""
        self.poisson_weight = poisson_weight
        self.mc_weight = mc_weight
        self.home_form_weight = home_form_weight
        self.away_form_weight = away_form_weight
        self.h2h_weight = h2h_weight
    
    def aggregate(self, sources: SourceProbabilities) -> AggregatedMarketsResult:
        """
        Aggregate all probabilities into chart-ready result.
        
        Args:
            sources: All probability inputs from various models
            
        Returns:
            AggregatedMarketsResult with all markets aggregated
        """
        # Adjust H2H weight based on reliability
        h2h_weight = self._get_adjusted_h2h_weight(sources.h2h_reliability)
        poisson_weight = self.poisson_weight + (self.h2h_weight - h2h_weight)
        
        # Aggregate each market
        over_25 = self._aggregate_market(
            market_type=MarketType.OVER_25,
            poisson=sources.poisson_over_25,
            mc=sources.mc_over_25,
            home_form=sources.home_form_over_25,
            away_form=sources.away_form_over_25,
            h2h=sources.h2h_over_25,
            poisson_weight=poisson_weight,
            h2h_weight=h2h_weight,
        )
        
        btts = self._aggregate_market(
            market_type=MarketType.BTTS,
            poisson=sources.poisson_btts,
            mc=sources.mc_btts,
            home_form=sources.home_form_btts,
            away_form=sources.away_form_btts,
            h2h=sources.h2h_btts,
            poisson_weight=poisson_weight,
            h2h_weight=h2h_weight,
        )
        
        goals_2_3 = self._aggregate_market(
            market_type=MarketType.GOALS_2_3,
            poisson=sources.poisson_goals_2_3,
            mc=sources.mc_goals_2_3,
            home_form=sources.home_form_goals_2_3,
            away_form=sources.away_form_goals_2_3,
            h2h=sources.h2h_goals_2_3,
            poisson_weight=poisson_weight,
            h2h_weight=h2h_weight,
        )
        
        # Result markets (form doesn't apply directly)
        home_win = self._aggregate_result_market(
            market_type=MarketType.HOME_WIN,
            poisson=sources.poisson_home_win,
            mc=sources.mc_home_win,
            h2h=sources.h2h_home_win,
            h2h_weight=h2h_weight,
        )
        
        away_win = self._aggregate_result_market(
            market_type=MarketType.AWAY_WIN,
            poisson=sources.poisson_away_win,
            mc=sources.mc_away_win,
            h2h=sources.h2h_away_win,
            h2h_weight=h2h_weight,
        )
        
        draw = self._aggregate_result_market(
            market_type=MarketType.DRAW,
            poisson=sources.poisson_draw,
            mc=sources.mc_draw,
            h2h=sources.h2h_draw,
            h2h_weight=h2h_weight,
        )
        
        # Calculate overall confidence
        all_markets = [over_25, btts, goals_2_3, home_win, away_win, draw]
        confidence_index = self._calculate_overall_confidence(all_markets)
        
        return AggregatedMarketsResult(
            over_25=over_25,
            btts=btts,
            goals_2_3=goals_2_3,
            home_win=home_win,
            away_win=away_win,
            draw=draw,
            overall_confidence_index=confidence_index,
        )
    
    def _aggregate_market(
        self,
        market_type: MarketType,
        poisson: float,
        mc: float,
        home_form: float,
        away_form: float,
        h2h: float,
        poisson_weight: float,
        h2h_weight: float,
    ) -> AggregatedMarket:
        """Aggregate a goals-based market (over_25, btts, goals_2_3)."""
        sources = [
            MarketSourceBreakdown("poisson", poisson, poisson_weight, poisson * poisson_weight),
            MarketSourceBreakdown("monte_carlo", mc, self.mc_weight, mc * self.mc_weight),
            MarketSourceBreakdown("home_form", home_form, self.home_form_weight, home_form * self.home_form_weight),
            MarketSourceBreakdown("away_form", away_form, self.away_form_weight, away_form * self.away_form_weight),
            MarketSourceBreakdown("h2h", h2h, h2h_weight, h2h * h2h_weight),
        ]
        
        # Calculate weighted sum
        final_prob = sum(s.weighted_contribution for s in sources)
        
        # Clamp to valid probability
        final_prob = max(0.0, min(1.0, final_prob))
        
        # Calculate variance for confidence
        probs = [poisson, mc, home_form, away_form, h2h]
        variance = statistics.variance(probs) if len(probs) > 1 else 0.0
        
        # Determine confidence
        confidence = self._calculate_confidence(probs)
        
        return AggregatedMarket(
            market=market_type,
            final_probability=final_prob,
            confidence=confidence,
            qualified=False,  # Default, calculated later
            sources=sources,
            source_variance=variance,
        )
    
    def _aggregate_result_market(
        self,
        market_type: MarketType,
        poisson: float,
        mc: float,
        h2h: float,
        h2h_weight: float,
    ) -> AggregatedMarket:
        """Aggregate a result market (home_win, away_win, draw)."""
        # For result markets, use Poisson + MC + H2H only
        # Redistribute form weights to Poisson/MC
        poisson_weight = 0.50 + (self.h2h_weight - h2h_weight) / 2
        mc_weight = 0.50 - h2h_weight
        
        sources = [
            MarketSourceBreakdown("poisson", poisson, poisson_weight, poisson * poisson_weight),
            MarketSourceBreakdown("monte_carlo", mc, mc_weight, mc * mc_weight),
            MarketSourceBreakdown("h2h", h2h, h2h_weight, h2h * h2h_weight),
        ]
        
        final_prob = sum(s.weighted_contribution for s in sources)
        final_prob = max(0.0, min(1.0, final_prob))
        
        probs = [poisson, mc, h2h]
        variance = statistics.variance(probs) if len(probs) > 1 else 0.0
        
        confidence = self._calculate_confidence(probs)
        
        return AggregatedMarket(
            market=market_type,
            final_probability=final_prob,
            confidence=confidence,
            qualified=False,  # Default, calculated later
            sources=sources,
            source_variance=variance,
        )
    
    def _get_adjusted_h2h_weight(self, reliability: float) -> float:
        """Adjust H2H weight based on reliability score."""
        if reliability < self.H2H_MIN_RELIABILITY:
            return 0.0
        return self.h2h_weight * reliability
    
    def _calculate_confidence(self, probabilities: List[float]) -> ConfidenceLevel:
        """
        Calculate confidence based on source agreement.
        
        HIGH: All sources within 10% of each other
        MEDIUM: Most sources agree
        LOW: Sources strongly disagree
        """
        if len(probabilities) < 2:
            return ConfidenceLevel.MEDIUM
        
        std_dev = statistics.stdev(probabilities)
        
        if std_dev < 0.10:
            return ConfidenceLevel.HIGH
        elif std_dev < 0.20:
            return ConfidenceLevel.MEDIUM
        else:
            return ConfidenceLevel.LOW
    
    def _calculate_overall_confidence(self, markets: List[AggregatedMarket]) -> int:
        """
        Calculate overall confidence index (0-100).
        
        Based on:
        - Average variance across markets
        - Number of HIGH confidence markets
        """
        if not markets:
            return 50
        
        # Count confidence levels
        high_count = sum(1 for m in markets if m.confidence == ConfidenceLevel.HIGH)
        medium_count = sum(1 for m in markets if m.confidence == ConfidenceLevel.MEDIUM)
        
        # Base score
        base = 40 + (high_count * 10) + (medium_count * 5)
        
        # Penalize high variance
        avg_variance = sum(m.source_variance for m in markets) / len(markets)
        variance_penalty = min(20, int(avg_variance * 100))
        
        score = base - variance_penalty
        return max(0, min(100, score))
