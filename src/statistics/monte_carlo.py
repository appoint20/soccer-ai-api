"""
Monte Carlo simulation for match outcome prediction.

Uses Poisson-based simulations to estimate:
- Draw probability more accurately
- Scoreline distributions
- Confidence intervals
"""
from typing import Dict, List, Any, Tuple
from datetime import date
import numpy as np
from collections import Counter

from src.statistics.dixon_coles_model import DixonColesModel
from src.utils.logger import get_logger


class MonteCarloPredictor:
    """
    Monte Carlo simulation for soccer predictions.
    
    Runs thousands of simulated matches to get more accurate
    probability estimates, especially for draws.
    """
    
    def __init__(
        self,
        n_simulations: int = 10000,
        xi: float = 0.01,
        rho: float = -0.10,
    ):
        """
        Initialize Monte Carlo predictor.
        
        Args:
            n_simulations: Number of simulations per match
            xi: Dixon-Coles time decay parameter
            rho: Dixon-Coles correlation parameter
        """
        self.logger = get_logger("MonteCarloPredictor")
        self.n_simulations = n_simulations
        
        # Use Dixon-Coles for expected goals
        self.dc_model = DixonColesModel(xi=xi, rho=rho)
        
        self.is_fitted = False
    
    def fit(self, matches: List[Dict], lookback_days: int = 365) -> None:
        """
        Fit underlying model to historical data.
        
        Args:
            matches: Historical match data
            lookback_days: Days of history to use
        """
        self.dc_model.fit(matches, lookback_days)
        self.is_fitted = True
        self.logger.info("Monte Carlo predictor fitted")
    
    def simulate_match(
        self,
        home_team: str,
        away_team: str,
    ) -> Tuple[np.ndarray, np.ndarray]:
        """
        Simulate a match n_simulations times.
        
        Uses Poisson distribution with expected goals from Dixon-Coles.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            
        Returns:
            Tuple of (home_goals_array, away_goals_array)
        """
        # Get expected goals
        home_xg, away_xg = self.dc_model.get_expected_goals(home_team, away_team)
        
        # Simulate goals using Poisson
        home_goals = np.random.poisson(home_xg, self.n_simulations)
        away_goals = np.random.poisson(away_xg, self.n_simulations)
        
        return home_goals, away_goals
    
    def predict_probabilities(
        self,
        home_team: str,
        away_team: str,
    ) -> Dict[str, float]:
        """
        Predict match probabilities via simulation.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            
        Returns:
            Dict with all probability estimates
        """
        home_goals, away_goals = self.simulate_match(home_team, away_team)
        n = self.n_simulations
        
        # 1X2 probabilities
        home_wins = np.sum(home_goals > away_goals) / n
        draws = np.sum(home_goals == away_goals) / n
        away_wins = np.sum(home_goals < away_goals) / n
        
        # Total goals
        total_goals = home_goals + away_goals
        over25 = np.sum(total_goals > 2.5) / n
        over15 = np.sum(total_goals > 1.5) / n
        over35 = np.sum(total_goals > 3.5) / n
        
        # BTTS
        btts = np.sum((home_goals > 0) & (away_goals > 0)) / n
        
        # Most likely scorelines
        scorelines = Counter(zip(home_goals, away_goals))
        top_scorelines = scorelines.most_common(5)
        
        # Expected values
        home_xg, away_xg = self.dc_model.get_expected_goals(home_team, away_team)
        
        return {
            "home_win": round(home_wins, 4),
            "draw": round(draws, 4),
            "away_win": round(away_wins, 4),
            "over25": round(over25, 4),
            "over15": round(over15, 4),
            "over35": round(over35, 4),
            "btts": round(btts, 4),
            "home_xg": round(home_xg, 2),
            "away_xg": round(away_xg, 2),
            "avg_total_goals": round(np.mean(total_goals), 2),
            "top_scorelines": [(f"{h}-{a}", round(c/n, 4)) for (h, a), c in top_scorelines],
        }
    
    def is_draw_likely(
        self,
        home_team: str,
        away_team: str,
        threshold: float = 0.28,
    ) -> Tuple[bool, float]:
        """
        Check if draw is likely based on simulation.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            threshold: Draw probability threshold
            
        Returns:
            Tuple of (is_draw_likely, draw_probability)
        """
        probs = self.predict_probabilities(home_team, away_team)
        draw_prob = probs["draw"]
        
        return draw_prob >= threshold, draw_prob
    
    def detect_draw_signals(
        self,
        home_team: str,
        away_team: str,
    ) -> Dict[str, Any]:
        """
        Detect signals that indicate a draw is likely.
        
        Signals:
        - Similar expected goals
        - High 0-0, 1-1 probability
        - Draw is top prediction
        - Close 1X2 probabilities
        
        Args:
            home_team: Home team name
            away_team: Away team name
            
        Returns:
            Dict with draw signals
        """
        probs = self.predict_probabilities(home_team, away_team)
        home_xg = probs["home_xg"]
        away_xg = probs["away_xg"]
        
        signals = []
        draw_score = 0.0
        
        # Signal 1: Similar expected goals (within 0.5)
        xg_diff = abs(home_xg - away_xg)
        if xg_diff < 0.5:
            signals.append(f"Similar xG ({home_xg:.2f} vs {away_xg:.2f})")
            draw_score += 0.2
        
        # Signal 2: High draw probability
        if probs["draw"] >= 0.28:
            signals.append(f"High draw prob ({probs['draw']:.1%})")
            draw_score += 0.3
        
        # Signal 3: Low total expected goals (defensive match)
        total_xg = home_xg + away_xg
        if total_xg < 2.3:
            signals.append(f"Low total xG ({total_xg:.2f})")
            draw_score += 0.15
        
        # Signal 4: 0-0 or 1-1 in top scorelines
        for scoreline, prob in probs["top_scorelines"]:
            if scoreline in ["0-0", "1-1"]:
                if prob >= 0.10:
                    signals.append(f"High {scoreline} probability ({prob:.1%})")
                    draw_score += 0.2
        
        # Signal 5: Close 1X2 probabilities (no clear favorite)
        max_prob = max(probs["home_win"], probs["draw"], probs["away_win"])
        if max_prob < 0.45:
            signals.append(f"No clear favorite (max={max_prob:.1%})")
            draw_score += 0.15
        
        return {
            "draw_probability": probs["draw"],
            "draw_score": min(draw_score, 1.0),
            "is_draw_likely": draw_score >= 0.5,
            "signals": signals,
            "probabilities": probs,
        }
    
    def predict(
        self,
        home_team: str,
        away_team: str,
    ) -> Dict[str, Any]:
        """
        Full prediction with draw detection.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            
        Returns:
            Complete prediction with draw analysis
        """
        probs = self.predict_probabilities(home_team, away_team)
        draw_analysis = self.detect_draw_signals(home_team, away_team)
        
        # Determine best prediction
        max_prob = max(probs["home_win"], probs["draw"], probs["away_win"])
        
        if probs["home_win"] == max_prob:
            prediction = "H"
        elif probs["away_win"] == max_prob:
            prediction = "A"
        else:
            prediction = "D"
        
        # Confidence based on margin
        second_max = sorted([probs["home_win"], probs["draw"], probs["away_win"]])[-2]
        confidence = max_prob - second_max
        
        return {
            "model": "monte_carlo",
            "prediction": prediction,
            "confidence": round(confidence, 4),
            "probabilities": {
                "home_win": probs["home_win"],
                "draw": probs["draw"],
                "away_win": probs["away_win"],
            },
            "over25": probs["over25"],
            "btts": probs["btts"],
            "expected_goals": {
                "home": probs["home_xg"],
                "away": probs["away_xg"],
                "total": round(probs["home_xg"] + probs["away_xg"], 2),
            },
            "top_scorelines": probs["top_scorelines"],
            "draw_analysis": draw_analysis,
        }
    
    def predict_with_team_stats(
        self,
        home_team: str,
        away_team: str,
        team_stats: Dict[str, Any],
    ) -> Dict[str, Any]:
        """
        Predict using Poisson simulation + team historical rates.
        
        Combines:
        - MC simulation probabilities (Poisson-based)
        - Team historical BTTS/Over25 rates (L9 + L6 venue)
        
        Formula:
        - final_prob = 0.6 * mc_prob + 0.4 * historical_rate
        
        Args:
            home_team: Home team name
            away_team: Away team name
            team_stats: Stats from MatchStatsService.calculate_match_stats()
            
        Returns:
            Enhanced prediction with combined probabilities
        """
        # Get Monte Carlo probabilities
        base_pred = self.predict(home_team, away_team)
        mc_over25 = base_pred["over25"]
        mc_btts = base_pred["btts"]
        
        # Extract historical rates
        btts = team_stats.get("btts", {})
        over25 = team_stats.get("over25", {})
        qualification = team_stats.get("qualification", {})
        
        # Home team rates (weighted: 40% overall, 60% venue)
        home_btts_rate = (
            btts.get("home_team", {}).get("overall_9", {}).get("pct", 50) * 0.4 +
            btts.get("home_team", {}).get("home_6", {}).get("pct", 50) * 0.6
        ) / 100.0
        
        away_btts_rate = (
            btts.get("away_team", {}).get("overall_9", {}).get("pct", 50) * 0.4 +
            btts.get("away_team", {}).get("away_6", {}).get("pct", 50) * 0.6
        ) / 100.0
        
        home_o25_rate = (
            over25.get("home_team", {}).get("overall_9", {}).get("pct", 50) * 0.4 +
            over25.get("home_team", {}).get("home_6", {}).get("pct", 50) * 0.6
        ) / 100.0
        
        away_o25_rate = (
            over25.get("away_team", {}).get("overall_9", {}).get("pct", 50) * 0.4 +
            over25.get("away_team", {}).get("away_6", {}).get("pct", 50) * 0.6
        ) / 100.0
        
        # Combined historical rates (average of both teams)
        hist_btts = (home_btts_rate + away_btts_rate) / 2
        hist_o25 = (home_o25_rate + away_o25_rate) / 2
        
        # Home/Away scored rates (for BTTS confidence)
        home_scored = btts.get("home_team", {}).get("scored_overall_9", {}).get("pct", 70) / 100.0
        away_scored = btts.get("away_team", {}).get("scored_overall_9", {}).get("pct", 70) / 100.0
        
        # Final probabilities: 60% MC + 40% historical
        final_btts = mc_btts * 0.6 + hist_btts * 0.4
        final_o25 = mc_over25 * 0.6 + hist_o25 * 0.4
        
        # Adjust based on scored rates (both teams must score for BTTS)
        # If one team rarely scores, reduce BTTS probability
        min_scored_rate = min(home_scored, away_scored)
        if min_scored_rate < 0.6:  # If one team scores < 60% of games
            final_btts *= (0.5 + min_scored_rate)  # Reduce proportionally
        
        return {
            **base_pred,
            "enhanced_btts": round(final_btts, 4),
            "enhanced_over25": round(final_o25, 4),
            "historical_rates": {
                "home_btts": round(home_btts_rate, 3),
                "away_btts": round(away_btts_rate, 3),
                "home_over25": round(home_o25_rate, 3),
                "away_over25": round(away_o25_rate, 3),
                "home_scored": round(home_scored, 3),
                "away_scored": round(away_scored, 3),
            },
            "qualification": {
                "btts_qualified": qualification.get("btts_qualified", False),
                "over25_qualified": qualification.get("over25_qualified", False),
            },
        }
