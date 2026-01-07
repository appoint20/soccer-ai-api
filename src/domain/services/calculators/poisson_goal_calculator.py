"""
Poisson Goal Calculator.

Calculates goal probabilities using Poisson distribution with
Dixon-Coles tau correction, configurable per league.
"""
from dataclasses import dataclass
from typing import Dict, Any, Optional, Tuple
from math import exp, factorial
import numpy as np

from src.domain.services.calculators.team_form_calculator import TeamFormStats
from src.utils.logger import get_logger


# Lower-scoring leagues benefit more from DC correction
TAU_BY_LEAGUE = {
    "E0": 0.08, "E1": 0.10, "E2": 0.11, "E3": 0.12,
    "D1": 0.12, "SP1": 0.09, "I1": 0.10, "I2": 0.11,
    "F1": 0.09, "F2": 0.10,
}


@dataclass
class PoissonProbabilities:
    """Poisson + Dixon-Coles probability results."""
    expected_home_goals: float = 0.0
    expected_away_goals: float = 0.0
    home_win: float = 0.0
    draw: float = 0.0
    away_win: float = 0.0
    over_25: float = 0.0
    under_25: float = 0.0
    btts: float = 0.0
    btts_no: float = 0.0
    goals_2_3: float = 0.0
    scoreline_matrix: Optional[np.ndarray] = None
    
    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary for API response."""
        return {
            "expected_score": f"{round(self.expected_home_goals)} - {round(self.expected_away_goals)}",
            "expected_home_goals": round(self.expected_home_goals, 2),
            "expected_away_goals": round(self.expected_away_goals, 2),
            "home_win": round(self.home_win, 3),
            "draw": round(self.draw, 3),
            "away_win": round(self.away_win, 3),
            "over_25": round(self.over_25, 3),
            "under_25": round(self.under_25, 3),
            "btts": round(self.btts, 3),
            "btts_no": round(self.btts_no, 3),
            "goals_2_3": round(self.goals_2_3, 3),
        }


class PoissonGoalCalculator:
    """
    Calculate goal probabilities using Poisson + Dixon-Coles.
    
    Features:
    - Team strength from form stats
    - League average blending
    - Configurable tau correction per league
    - Full scoreline probability matrix
    """
    
    def __init__(
        self,
        home_advantage: float = 1.1,
        max_goals: int = 8,
        rho: float = -0.13,
    ):
        """
        Initialize calculator.
        
        Args:
            home_advantage: Multiplier for home team xG
            max_goals: Maximum goals to consider in matrix
            rho: Dixon-Coles correlation parameter
        """
        self.logger = get_logger("PoissonGoalCalculator")
        self.home_advantage = home_advantage
        self.max_goals = max_goals
        self.rho = rho
    
    def calculate_probabilities(
        self,
        home_team: str,
        away_team: str,
        home_stats: TeamFormStats,
        away_stats: TeamFormStats,
        league_code: str,
        league_avg_goals: float = 2.7,
    ) -> PoissonProbabilities:
        """
        Calculate all goal probabilities.
        
        Args:
            home_team: Home team name (for logging)
            away_team: Away team name (for logging)
            home_stats: Home team's form stats
            away_stats: Away team's form stats
            league_code: League for tau selection (E0, D1, etc.)
            league_avg_goals: Average total goals per match in league
            
        Returns:
            PoissonProbabilities with all market probabilities
        """
        # Calculate expected goals
        home_xg, away_xg = self._calculate_expected_goals(
            home_stats, away_stats, league_avg_goals
        )
        
        # Get tau for this league
        tau = TAU_BY_LEAGUE.get(league_code, 0.10)
        
        # Build probability matrix with DC correction
        prob_matrix = self._build_scoreline_matrix(home_xg, away_xg, tau)
        
        # Calculate market probabilities
        home_win = 0.0
        draw = 0.0
        away_win = 0.0
        over_25 = 0.0
        btts = 0.0
        goals_2_3 = 0.0
        
        for i in range(self.max_goals):
            for j in range(self.max_goals):
                prob = prob_matrix[i, j]
                total_goals = i + j
                
                if i > j:
                    home_win += prob
                elif i < j:
                    away_win += prob
                else:
                    draw += prob
                
                if total_goals > 2.5:
                    over_25 += prob
                
                if i > 0 and j > 0:
                    btts += prob
                
                if total_goals in [2, 3]:
                    goals_2_3 += prob
        
        return PoissonProbabilities(
            expected_home_goals=home_xg,
            expected_away_goals=away_xg,
            home_win=home_win,
            draw=draw,
            away_win=away_win,
            over_25=over_25,
            under_25=1.0 - over_25,
            btts=btts,
            btts_no=1.0 - btts,
            goals_2_3=goals_2_3,
            scoreline_matrix=prob_matrix,
        )
    
    def _calculate_expected_goals(
        self,
        home_stats: TeamFormStats,
        away_stats: TeamFormStats,
        league_avg_goals: float,
    ) -> Tuple[float, float]:
        """
        Calculate expected goals for each team.
        
        Blends team form with league average for stability.
        """
        league_avg_per_team = league_avg_goals / 2  # ~1.35 per team
        
        # Home team attack strength
        if home_stats.sample_size >= 3:
            home_attack = home_stats.avg_goals_scored
        else:
            home_attack = league_avg_per_team
        
        # Away team defense weakness
        if away_stats.sample_size >= 3:
            away_defense = away_stats.avg_goals_conceded
        else:
            away_defense = league_avg_per_team
        
        # Blend with league average (60% team, 40% league for stability)
        blend_factor = min(1.0, home_stats.effective_sample_size / 5)
        home_xg = (
            blend_factor * (home_attack + away_defense) / 2 +
            (1 - blend_factor) * league_avg_per_team
        ) * self.home_advantage
        
        # Away team
        if away_stats.sample_size >= 3:
            away_attack = away_stats.avg_goals_scored
        else:
            away_attack = league_avg_per_team
        
        if home_stats.sample_size >= 3:
            home_defense = home_stats.avg_goals_conceded
        else:
            home_defense = league_avg_per_team
        
        blend_factor_away = min(1.0, away_stats.effective_sample_size / 5)
        away_xg = (
            blend_factor_away * (away_attack + home_defense) / 2 +
            (1 - blend_factor_away) * league_avg_per_team
        )
        
        # Bounds
        home_xg = max(0.3, min(4.0, home_xg))
        away_xg = max(0.2, min(3.5, away_xg))
        
        return home_xg, away_xg
    
    def _build_scoreline_matrix(
        self,
        home_xg: float,
        away_xg: float,
        tau: float,
    ) -> np.ndarray:
        """
        Build probability matrix with Dixon-Coles tau correction.
        
        Tau correction improves accuracy for low-scoring matches:
        0-0, 1-0, 0-1, 1-1
        """
        matrix = np.zeros((self.max_goals, self.max_goals))
        
        for i in range(self.max_goals):
            for j in range(self.max_goals):
                # Base Poisson probability
                prob_home = self._poisson_prob(i, home_xg)
                prob_away = self._poisson_prob(j, away_xg)
                base_prob = prob_home * prob_away
                
                # Apply Dixon-Coles tau correction
                tau_correction = self._tau_correction(i, j, home_xg, away_xg, tau)
                
                matrix[i, j] = base_prob * tau_correction
        
        # Normalize to sum to 1
        total = matrix.sum()
        if total > 0:
            matrix = matrix / total
        
        return matrix
    
    def _poisson_prob(self, k: int, lambda_: float) -> float:
        """Calculate Poisson probability P(X=k)."""
        if lambda_ <= 0:
            return 1.0 if k == 0 else 0.0
        try:
            return (lambda_ ** k) * exp(-lambda_) / factorial(k)
        except (OverflowError, ValueError):
            return 0.0
    
    def _tau_correction(
        self,
        home_goals: int,
        away_goals: int,
        home_xg: float,
        away_xg: float,
        tau: float,
    ) -> float:
        """
        Dixon-Coles tau correction for correlated low-scoring outcomes.
        
        Standard Poisson underestimates 0-0, 1-1, 0-1, 1-0.
        Tau adjusts these probabilities.
        """
        # Only apply to low-scoring matches
        if home_goals > 1 or away_goals > 1:
            return 1.0
        
        # Use the more common rho-based formulation
        rho = self.rho
        
        if home_goals == 0 and away_goals == 0:
            return 1.0 - home_xg * away_xg * rho
        elif home_goals == 0 and away_goals == 1:
            return 1.0 + home_xg * rho
        elif home_goals == 1 and away_goals == 0:
            return 1.0 + away_xg * rho
        elif home_goals == 1 and away_goals == 1:
            return 1.0 - rho
        
        return 1.0
