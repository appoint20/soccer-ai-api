"""
Monte Carlo Uncertainty Adjuster.

Applies mean reversion with capped adjustment (±7%) to Poisson probabilities.
Poisson = SIGNAL, Monte Carlo = UNCERTAINTY CALIBRATION.
"""
from dataclasses import dataclass
from typing import List, Dict, Any, Optional, Tuple
import numpy as np
from scipy import stats

from src.utils.logger import get_logger


@dataclass
class MonteCarloResult:
    """Monte Carlo adjustment result for a single market."""
    adjusted_probability: float = 0.0
    confidence_lower: float = 0.0
    confidence_upper: float = 0.0
    streak_length: int = 0
    regression_applied: bool = False
    adjustment_amount: float = 0.0
    
    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary for API response."""
        return {
            "adjusted_probability": round(self.adjusted_probability, 3),
            "confidence_lower": round(self.confidence_lower, 3),
            "confidence_upper": round(self.confidence_upper, 3),
            "streak_length": self.streak_length,
            "regression_applied": self.regression_applied,
        }


@dataclass
class MonteCarloResults:
    """Monte Carlo results for all markets."""
    over_25: MonteCarloResult
    btts: MonteCarloResult
    home_win: MonteCarloResult
    away_win: MonteCarloResult
    draw: MonteCarloResult
    goals_2_3: MonteCarloResult
    
    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary for API response."""
        return {
            "over_25": self.over_25.to_dict(),
            "btts": self.btts.to_dict(),
            "home_win": self.home_win.to_dict(),
            "away_win": self.away_win.to_dict(),
            "draw": self.draw.to_dict(),
            "goals_2_3": self.goals_2_3.to_dict(),
        }


class MonteCarloUncertaintyAdjuster:
    """
    Monte Carlo with CAPPED mean reversion.
    
    Key principle:
    - Poisson = SIGNAL (primary source of truth)
    - Monte Carlo = UNCERTAINTY CALIBRATION (not a second opinion)
    
    Never allows MC to overpower Poisson by more than MAX_ADJUSTMENT.
    """
    
    MAX_ADJUSTMENT = 0.07  # ±7% max deviation from Poisson
    STREAK_THRESHOLD = 3   # Apply regression after 3+ consecutive outcomes
    
    def __init__(
        self,
        n_simulations: int = 10000,
        max_adjustment: float = 0.07,
    ):
        """
        Initialize adjuster.
        
        Args:
            n_simulations: Number of Monte Carlo simulations
            max_adjustment: Maximum probability adjustment (±)
        """
        self.logger = get_logger("MonteCarloUncertaintyAdjuster")
        self.n_simulations = n_simulations
        self.max_adjustment = max_adjustment
    
    def adjust_probability(
        self,
        base_probability: float,
        recent_outcomes: List[bool],
        market_type: str,
    ) -> MonteCarloResult:
        """
        Adjust probability with capped mean reversion.
        
        Args:
            base_probability: Probability from Poisson model
            recent_outcomes: List of recent match outcomes for this market
                            (True = event occurred, e.g., Over 2.5 happened)
            market_type: Market identifier for logging
            
        Returns:
            MonteCarloResult with adjusted probability and metadata
        """
        if not recent_outcomes:
            return MonteCarloResult(
                adjusted_probability=base_probability,
                confidence_lower=max(0, base_probability - 0.1),
                confidence_upper=min(1, base_probability + 0.1),
                streak_length=0,
                regression_applied=False,
            )
        
        # Detect streak
        streak_length, streak_value = self._detect_streak(recent_outcomes)
        
        # Calculate empirical rate from recent matches
        empirical_rate = sum(recent_outcomes) / len(recent_outcomes)
        
        # Calculate adjustment based on mean reversion
        adjustment = 0.0
        regression_applied = False
        
        if streak_length >= self.STREAK_THRESHOLD:
            # Recent outcomes are consistently one way
            # Apply mean reversion toward base rate
            if streak_value:
                # Streak of True (e.g., Over 2.5 happening often)
                # Slightly increase Under probability (decrease Over)
                adjustment = -min(
                    self.max_adjustment,
                    (empirical_rate - base_probability) * 0.3
                )
            else:
                # Streak of False (e.g., Under 2.5 happening often)
                # Slightly increase Over probability
                adjustment = min(
                    self.max_adjustment,
                    (base_probability - empirical_rate) * 0.3
                )
            regression_applied = True
        
        # Cap adjustment
        adjustment = max(-self.max_adjustment, min(self.max_adjustment, adjustment))
        adjusted_probability = base_probability + adjustment
        
        # Ensure bounds
        adjusted_probability = max(0.01, min(0.99, adjusted_probability))
        
        # Calculate confidence interval using Beta distribution
        # Use empirical data to inform uncertainty
        n = len(recent_outcomes)
        successes = sum(recent_outcomes)
        
        # Beta distribution for confidence interval
        alpha = successes + 1
        beta = (n - successes) + 1
        
        try:
            ci_lower = stats.beta.ppf(0.1, alpha, beta)
            ci_upper = stats.beta.ppf(0.9, alpha, beta)
        except Exception:
            ci_lower = max(0, adjusted_probability - 0.15)
            ci_upper = min(1, adjusted_probability + 0.15)
        
        return MonteCarloResult(
            adjusted_probability=adjusted_probability,
            confidence_lower=ci_lower,
            confidence_upper=ci_upper,
            streak_length=streak_length,
            regression_applied=regression_applied,
            adjustment_amount=adjustment,
        )
    
    def calculate_all_markets(
        self,
        poisson_probs: Dict[str, float],
        recent_outcomes: Dict[str, List[bool]],
    ) -> MonteCarloResults:
        """
        Calculate MC adjustments for all markets.
        
        Args:
            poisson_probs: Dict with keys over_25, btts, home_win, etc.
            recent_outcomes: Dict with same keys, values are list of bools
            
        Returns:
            MonteCarloResults with all market adjustments
        """
        markets = ["over_25", "btts", "home_win", "away_win", "draw", "goals_2_3"]
        results = {}
        
        for market in markets:
            base_prob = poisson_probs.get(market, 0.5)
            outcomes = recent_outcomes.get(market, [])
            results[market] = self.adjust_probability(base_prob, outcomes, market)
        
        return MonteCarloResults(
            over_25=results["over_25"],
            btts=results["btts"],
            home_win=results["home_win"],
            away_win=results["away_win"],
            draw=results["draw"],
            goals_2_3=results["goals_2_3"],
        )
    
    def _detect_streak(
        self,
        outcomes: List[bool],
    ) -> Tuple[int, Optional[bool]]:
        """
        Detect the most recent streak of consecutive outcomes.
        
        Returns:
            (streak_length, streak_value)
            streak_value is True/False indicating what the streak consists of
        """
        if not outcomes:
            return 0, None
        
        # Most recent first
        streak_value = outcomes[0]
        streak_length = 0
        
        for outcome in outcomes:
            if outcome == streak_value:
                streak_length += 1
            else:
                break
        
        return streak_length, streak_value
    
    def simulate_match_outcomes(
        self,
        home_xg: float,
        away_xg: float,
    ) -> Dict[str, float]:
        """
        Run Monte Carlo simulation for match outcomes.
        
        Returns probabilities based on simulation rather than analytics.
        Used for cross-validation, not primary prediction.
        """
        rng = np.random.default_rng()
        
        # Simulate goals
        home_goals = rng.poisson(home_xg, self.n_simulations)
        away_goals = rng.poisson(away_xg, self.n_simulations)
        
        total_goals = home_goals + away_goals
        
        # Calculate probabilities from simulations
        over_25 = np.mean(total_goals > 2.5)
        btts = np.mean((home_goals > 0) & (away_goals > 0))
        home_win = np.mean(home_goals > away_goals)
        away_win = np.mean(away_goals > home_goals)
        draw = np.mean(home_goals == away_goals)
        goals_2_3 = np.mean((total_goals == 2) | (total_goals == 3))
        
        return {
            "over_25": float(over_25),
            "btts": float(btts),
            "home_win": float(home_win),
            "away_win": float(away_win),
            "draw": float(draw),
            "goals_2_3": float(goals_2_3),
        }
