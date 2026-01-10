"""
Poisson model for soccer score prediction.

Implements the basic Poisson distribution model for predicting
match outcomes based on expected goals.
"""
from typing import Dict, List, Any, Optional, Tuple
from datetime import date, datetime
from math import exp, factorial
import numpy as np

from src.utils.logger import get_logger


class PoissonModel:
    """
    Basic Poisson model for soccer predictions.
    
    Estimates expected goals for each team and uses Poisson
    distribution to calculate scoreline probabilities.
    """
    
    def __init__(self):
        """Initialize Poisson model."""
        self.logger = get_logger("PoissonModel")
        
        # League average parameters
        self.avg_home_goals = 1.5
        self.avg_away_goals = 1.2
        
        # Team attack/defense strengths (relative to average)
        self.attack_strength: Dict[str, float] = {}
        self.defense_strength: Dict[str, float] = {}
        
        self.is_fitted = False
    
    def fit(self, matches: List[Dict], lookback_days: int = 365) -> None:
        """
        Fit model to historical match data.
        
        Calculates attack and defense strength for each team
        relative to league averages.
        
        Args:
            matches: List of historical matches
            lookback_days: Days of history to use
        """
        self.logger.info(f"Fitting Poisson model on {len(matches)} matches")
        
        # Filter to relevant time period
        cutoff = None
        if lookback_days:
            try:
                if matches:
                    dates = []
                    for m in matches:
                        d = m.get("match_date")
                        if isinstance(d, str):
                            d = datetime.fromisoformat(d[:10]).date()
                        if isinstance(d, date):
                            dates.append(d)
                    if dates:
                        latest = max(dates)
                        from datetime import timedelta
                        cutoff = latest - timedelta(days=lookback_days)
            except:
                pass
        
        filtered = []
        total_home = 0
        total_away = 0
        
        for m in matches:
            fthg = m.get("fthg")
            ftag = m.get("ftag")
            
            if fthg is None or ftag is None:
                continue
            
            # Apply time filter
            if cutoff:
                d = m.get("match_date")
                if isinstance(d, str):
                    try:
                        d = datetime.fromisoformat(d[:10]).date()
                    except:
                        pass
                if isinstance(d, date) and d < cutoff:
                    continue
            
            filtered.append(m)
            total_home += fthg
            total_away += ftag
        
        if not filtered:
            self.logger.warning("No valid matches for fitting")
            return
        
        n = len(filtered)
        self.avg_home_goals = total_home / n
        self.avg_away_goals = total_away / n
        
        # Calculate team-specific strengths
        team_home_scored: Dict[str, List] = {}
        team_away_scored: Dict[str, List] = {}
        team_home_conceded: Dict[str, List] = {}
        team_away_conceded: Dict[str, List] = {}
        
        for m in filtered:
            home = m.get("home_team", "")
            away = m.get("away_team", "")
            fthg = m.get("fthg", 0)
            ftag = m.get("ftag", 0)
            
            team_home_scored.setdefault(home, []).append(fthg)
            team_home_conceded.setdefault(home, []).append(ftag)
            team_away_scored.setdefault(away, []).append(ftag)
            team_away_conceded.setdefault(away, []).append(fthg)
        
        # Calculate attack strength = team avg / league avg
        for team in set(team_home_scored.keys()) | set(team_away_scored.keys()):
            home_scored = team_home_scored.get(team, [])
            away_scored = team_away_scored.get(team, [])
            
            all_scored = home_scored + away_scored
            if all_scored:
                team_avg_scored = sum(all_scored) / len(all_scored)
                league_avg = (self.avg_home_goals + self.avg_away_goals) / 2
                self.attack_strength[team] = team_avg_scored / league_avg if league_avg > 0 else 1.0
            else:
                self.attack_strength[team] = 1.0
            
            # Defense strength (lower = better)
            home_conceded = team_home_conceded.get(team, [])
            away_conceded = team_away_conceded.get(team, [])
            
            all_conceded = home_conceded + away_conceded
            if all_conceded:
                team_avg_conceded = sum(all_conceded) / len(all_conceded)
                self.defense_strength[team] = team_avg_conceded / league_avg if league_avg > 0 else 1.0
            else:
                self.defense_strength[team] = 1.0
        
        self.is_fitted = True
        self.logger.info(
            f"Fitted model: avg_home={self.avg_home_goals:.2f}, "
            f"avg_away={self.avg_away_goals:.2f}, teams={len(self.attack_strength)}"
        )
    
    def _find_team_key(self, team_name: str) -> str:
        """Find matching team key using fuzzy matching."""
        # Exact match
        if team_name in self.attack_strength:
            return team_name
        
        # Normalize and try common variations
        team_lower = team_name.lower().strip()
        
        for key in self.attack_strength.keys():
            key_lower = key.lower().strip()
            
            # Case-insensitive exact match
            if team_lower == key_lower:
                return key
            
            # Substring match (e.g., "Man United" in "Manchester United")
            if team_lower in key_lower or key_lower in team_lower:
                return key
            
            # First word match (e.g., "Newcastle" matches "Newcastle United")
            if team_lower.split()[0] == key_lower.split()[0]:
                return key
        
        return team_name  # Return original if no match
    
    def get_expected_goals(
        self,
        home_team: str,
        away_team: str,
    ) -> Tuple[float, float]:
        """
        Calculate expected goals for each team.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            
        Returns:
            Tuple of (home_xg, away_xg)
        """
        # Use fuzzy matching to find team keys
        home_key = self._find_team_key(home_team)
        away_key = self._find_team_key(away_team)
        
        # Get team strengths (default to average)
        home_attack = self.attack_strength.get(home_key, 1.0)
        home_defense = self.defense_strength.get(home_key, 1.0)
        away_attack = self.attack_strength.get(away_key, 1.0)
        away_defense = self.defense_strength.get(away_key, 1.0)
        
        # Expected goals = league_avg * attack_strength * opp_defense
        home_xg = self.avg_home_goals * home_attack * away_defense
        away_xg = self.avg_away_goals * away_attack * home_defense
        
        return home_xg, away_xg
    
    def _poisson_prob(self, k: int, lambda_: float) -> float:
        """Calculate Poisson probability P(X=k)."""
        if lambda_ <= 0:
            return 1.0 if k == 0 else 0.0
        return (lambda_ ** k) * exp(-lambda_) / factorial(k)
    
    def predict_scoreline_probs(
        self,
        home_team: str,
        away_team: str,
        max_goals: int = 7,
    ) -> np.ndarray:
        """
        Calculate probability matrix for all scorelines.
        
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
                probs[i, j] = (
                    self._poisson_prob(i, home_xg) *
                    self._poisson_prob(j, away_xg)
                )
        
        return probs
    
    def predict_1x2(
        self,
        home_team: str,
        away_team: str,
    ) -> Dict[str, float]:
        """
        Predict 1X2 probabilities.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            
        Returns:
            Dict with home_win, draw, away_win probabilities
        """
        probs = self.predict_scoreline_probs(home_team, away_team)
        
        home_win = 0.0
        draw = 0.0
        away_win = 0.0
        
        for i in range(probs.shape[0]):
            for j in range(probs.shape[1]):
                if i > j:
                    home_win += probs[i, j]
                elif i == j:
                    draw += probs[i, j]
                else:
                    away_win += probs[i, j]
        
        return {
            "home_win": round(home_win, 4),
            "draw": round(draw, 4),
            "away_win": round(away_win, 4),
        }
    
    def predict_over25_prob(
        self,
        home_team: str,
        away_team: str,
    ) -> float:
        """
        Predict probability of Over 2.5 goals.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            
        Returns:
            Probability of total goals > 2.5
        """
        probs = self.predict_scoreline_probs(home_team, away_team)
        
        under_prob = 0.0
        for i in range(probs.shape[0]):
            for j in range(probs.shape[1]):
                if i + j <= 2:
                    under_prob += probs[i, j]
        
        return round(1.0 - under_prob, 4)
    
    def predict_btts_prob(
        self,
        home_team: str,
        away_team: str,
    ) -> float:
        """
        Predict probability of Both Teams To Score.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            
        Returns:
            Probability of both teams scoring at least 1
        """
        probs = self.predict_scoreline_probs(home_team, away_team)
        
        btts_prob = 0.0
        for i in range(1, probs.shape[0]):
            for j in range(1, probs.shape[1]):
                btts_prob += probs[i, j]
        
        return round(btts_prob, 4)
    
    def predict(
        self,
        home_team: str,
        away_team: str,
    ) -> Dict[str, Any]:
        """
        Generate all predictions for a match.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            
        Returns:
            Dict with all predictions and expected goals
        """
        home_xg, away_xg = self.get_expected_goals(home_team, away_team)
        result_probs = self.predict_1x2(home_team, away_team)
        
        return {
            "home_xg": round(home_xg, 2),
            "away_xg": round(away_xg, 2),
            "total_xg": round(home_xg + away_xg, 2),
            "result": result_probs,
            "over25_prob": self.predict_over25_prob(home_team, away_team),
            "btts_prob": self.predict_btts_prob(home_team, away_team),
        }
