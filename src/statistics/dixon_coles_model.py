"""
Dixon-Coles model for improved soccer predictions.

Extends basic Poisson with:
- Tau correction for low-scoring draws (0-0, 1-1, 0-1, 1-0)
- Time decay weighting (recent matches = more weight)
- Better handling of home advantage
"""
from typing import Dict, List, Any, Optional, Tuple
from datetime import date, datetime, timedelta
from math import exp
import numpy as np

from src.statistics.poisson_model import PoissonModel
from src.utils.logger import get_logger


class DixonColesModel(PoissonModel):
    """
    Dixon-Coles improvements on basic Poisson model.
    
    Key improvements:
    1. Tau correction for low-scoring matches
    2. Time decay weighting
    3. Explicit home advantage parameter
    """
    
    def __init__(self, xi: float = 0.005, rho: float = -0.13):
        """
        Initialize Dixon-Coles model.
        
        Args:
            xi: Time decay parameter (higher = faster decay)
            rho: Correlation parameter for tau correction
        """
        super().__init__()
        self.logger = get_logger("DixonColesModel")
        
        # Dixon-Coles specific parameters
        self.xi = xi  # Time decay
        self.rho = rho  # Dependency parameter (typically negative)
        self.home_advantage = 1.0  # Multiplicative home factor
    
    def _time_decay_weight(
        self,
        match_date: date,
        reference_date: date,
    ) -> float:
        """
        Calculate time decay weight for a match.
        
        Weight = exp(-xi * days_diff)
        
        Args:
            match_date: Date of the match
            reference_date: Reference date (usually latest match)
            
        Returns:
            Weight between 0 and 1
        """
        if not isinstance(match_date, date):
            return 1.0
        
        days_diff = (reference_date - match_date).days
        if days_diff < 0:
            return 1.0
        
        return exp(-self.xi * days_diff)
    
    def _tau_correction(
        self,
        home_goals: int,
        away_goals: int,
        lambda_h: float,
        lambda_a: float,
    ) -> float:
        """
        Calculate tau correction factor for low-scoring matches.
        
        Dixon & Coles showed that independent Poisson underestimates
        0-0, 1-1, 0-1, 1-0 results due to score correlation.
        
        Args:
            home_goals: Home goals
            away_goals: Away goals
            lambda_h: Home expected goals
            lambda_a: Away expected goals
            
        Returns:
            Correction multiplier (tau)
        """
        rho = self.rho
        
        if home_goals == 0 and away_goals == 0:
            return 1 - lambda_h * lambda_a * rho
        elif home_goals == 0 and away_goals == 1:
            return 1 + lambda_h * rho
        elif home_goals == 1 and away_goals == 0:
            return 1 + lambda_a * rho
        elif home_goals == 1 and away_goals == 1:
            return 1 - rho
        else:
            return 1.0
    
    def fit(self, matches: List[Dict], lookback_days: int = 365) -> None:
        """
        Fit Dixon-Coles model with time decay.
        
        Args:
            matches: List of historical matches
            lookback_days: Days of history to use
        """
        self.logger.info(f"Fitting Dixon-Coles model on {len(matches)} matches")
        
        # Get reference date (latest match)
        reference_date = date.today()
        dates = []
        for m in matches:
            d = m.get("match_date")
            if isinstance(d, str):
                try:
                    d = datetime.fromisoformat(d[:10]).date()
                except:
                    continue
            if isinstance(d, date):
                dates.append(d)
        
        if dates:
            reference_date = max(dates)
        
        # Calculate weighted averages
        total_weight = 0.0
        weighted_home_goals = 0.0
        weighted_away_goals = 0.0
        
        # Team-specific weighted stats
        team_home_scored: Dict[str, Tuple[float, float]] = {}  # (weighted_sum, weight_sum)
        team_away_scored: Dict[str, Tuple[float, float]] = {}
        team_home_conceded: Dict[str, Tuple[float, float]] = {}
        team_away_conceded: Dict[str, Tuple[float, float]] = {}
        
        cutoff = reference_date - timedelta(days=lookback_days) if lookback_days else None
        
        for m in matches:
            fthg = m.get("fthg")
            ftag = m.get("ftag")
            home = m.get("home_team", "")
            away = m.get("away_team", "")
            
            if fthg is None or ftag is None:
                continue
            
            # Get match date
            d = m.get("match_date")
            if isinstance(d, str):
                try:
                    d = datetime.fromisoformat(d[:10]).date()
                except:
                    d = None
            
            # Apply lookback filter
            if cutoff and isinstance(d, date) and d < cutoff:
                continue
            
            # Calculate weight
            weight = self._time_decay_weight(d, reference_date) if isinstance(d, date) else 0.5
            
            total_weight += weight
            weighted_home_goals += fthg * weight
            weighted_away_goals += ftag * weight
            
            # Team stats
            h_scored, h_weight = team_home_scored.get(home, (0.0, 0.0))
            team_home_scored[home] = (h_scored + fthg * weight, h_weight + weight)
            
            h_conceded, h_weight = team_home_conceded.get(home, (0.0, 0.0))
            team_home_conceded[home] = (h_conceded + ftag * weight, h_weight + weight)
            
            a_scored, a_weight = team_away_scored.get(away, (0.0, 0.0))
            team_away_scored[away] = (a_scored + ftag * weight, a_weight + weight)
            
            a_conceded, a_weight = team_away_conceded.get(away, (0.0, 0.0))
            team_away_conceded[away] = (a_conceded + fthg * weight, a_weight + weight)
        
        if total_weight == 0:
            self.logger.warning("No valid matches for fitting")
            return
        
        self.avg_home_goals = weighted_home_goals / total_weight
        self.avg_away_goals = weighted_away_goals / total_weight
        
        # Calculate home advantage
        self.home_advantage = self.avg_home_goals / self.avg_away_goals if self.avg_away_goals > 0 else 1.2
        
        # Calculate team strengths using weighted averages
        league_avg_goals = (self.avg_home_goals + self.avg_away_goals) / 2
        
        all_teams = set(team_home_scored.keys()) | set(team_away_scored.keys())
        
        for team in all_teams:
            # Attack strength
            home_scored_sum, home_scored_weight = team_home_scored.get(team, (0.0, 0.0))
            away_scored_sum, away_scored_weight = team_away_scored.get(team, (0.0, 0.0))
            
            total_scored_weight = home_scored_weight + away_scored_weight
            if total_scored_weight > 0:
                team_avg_scored = (home_scored_sum + away_scored_sum) / total_scored_weight
                self.attack_strength[team] = team_avg_scored / league_avg_goals if league_avg_goals > 0 else 1.0
            else:
                self.attack_strength[team] = 1.0
            
            # Defense strength
            home_conceded_sum, home_conceded_weight = team_home_conceded.get(team, (0.0, 0.0))
            away_conceded_sum, away_conceded_weight = team_away_conceded.get(team, (0.0, 0.0))
            
            total_conceded_weight = home_conceded_weight + away_conceded_weight
            if total_conceded_weight > 0:
                team_avg_conceded = (home_conceded_sum + away_conceded_sum) / total_conceded_weight
                self.defense_strength[team] = team_avg_conceded / league_avg_goals if league_avg_goals > 0 else 1.0
            else:
                self.defense_strength[team] = 1.0
        
        self.is_fitted = True
        self.logger.info(
            f"Fitted Dixon-Coles: avg_home={self.avg_home_goals:.2f}, "
            f"avg_away={self.avg_away_goals:.2f}, "
            f"home_advantage={self.home_advantage:.2f}, "
            f"teams={len(self.attack_strength)}"
        )
    
    def predict_scoreline_probs(
        self,
        home_team: str,
        away_team: str,
        max_goals: int = 7,
    ) -> np.ndarray:
        """
        Calculate corrected probability matrix for all scorelines.
        
        Applies tau correction for low-scoring matches.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            max_goals: Maximum goals to consider
            
        Returns:
            2D array where [i,j] = P(home=i, away=j)
        """
        home_xg, away_xg = self.get_expected_goals(home_team, away_team)
        
        probs = np.zeros((max_goals + 1, max_goals + 1))
        
        for i in range(max_goals + 1):
            for j in range(max_goals + 1):
                base_prob = (
                    self._poisson_prob(i, home_xg) *
                    self._poisson_prob(j, away_xg)
                )
                
                # Apply tau correction for low-scoring matches
                tau = self._tau_correction(i, j, home_xg, away_xg)
                probs[i, j] = base_prob * tau
        
        # Normalize to ensure probabilities sum to 1
        total = probs.sum()
        if total > 0:
            probs = probs / total
        
        return probs
    
    def predict(
        self,
        home_team: str,
        away_team: str,
    ) -> Dict[str, Any]:
        """
        Generate all predictions with Dixon-Coles corrections.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            
        Returns:
            Dict with all predictions
        """
        home_xg, away_xg = self.get_expected_goals(home_team, away_team)
        result_probs = self.predict_1x2(home_team, away_team)
        
        return {
            "model": "dixon_coles",
            "home_xg": round(home_xg, 2),
            "away_xg": round(away_xg, 2),
            "total_xg": round(home_xg + away_xg, 2),
            "result": result_probs,
            "over25_prob": self.predict_over25_prob(home_team, away_team),
            "btts_prob": self.predict_btts_prob(home_team, away_team),
            "home_advantage": round(self.home_advantage, 2),
        }

    def predict_match(self, home_team: str, away_team: str) -> Dict[str, float]:
        """
        Predict match outcomes for API response.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            
        Returns:
            Dict matching PoissonDistribution schema
        """
        home_xg, away_xg = self.get_expected_goals(home_team, away_team)
        result_probs = self.predict_1x2(home_team, away_team)
        over25 = self.predict_over25_prob(home_team, away_team)
        btts = self.predict_btts_prob(home_team, away_team)
        
        return {
            "home_win": result_probs["home_win"],
            "draw": result_probs["draw"],
            "away_win": result_probs["away_win"],
            "over25": over25,
            "btts": btts,
            "expected_home_goals": home_xg,
            "expected_away_goals": away_xg
        }
