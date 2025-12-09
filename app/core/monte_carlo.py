"""
Monte Carlo Simulation Predictor
"""
import numpy as np
from collections import Counter
from typing import Dict


class MonteCarloSimulator:
    """Probabilistic match simulation using Monte Carlo method"""
    
    def simulate(self, home_attack: float, away_attack: float,
                 home_defense: float, away_defense: float,
                 n_simulations: int = 10000,
                 home_advantage: float = 1.3) -> Dict:
        """
        Simulate match N times using Poisson-based random sampling.
        """
        # Expected goals
        home_lambda = home_attack * away_defense * home_advantage
        away_lambda = away_attack * home_defense
        
        # Clamp values
        home_lambda = max(0.5, min(4.0, home_lambda))
        away_lambda = max(0.5, min(4.0, away_lambda))
        
        # Run simulations
        home_goals = np.random.poisson(home_lambda, n_simulations)
        away_goals = np.random.poisson(away_lambda, n_simulations)
        
        # Calculate results
        home_wins = np.sum(home_goals > away_goals)
        draws = np.sum(home_goals == away_goals)
        away_wins = np.sum(home_goals < away_goals)
        
        hdw_probs = {
            'H': home_wins / n_simulations,
            'D': draws / n_simulations,
            'A': away_wins / n_simulations
        }
        
        # Over/Under
        total_goals = home_goals + away_goals
        over_25 = np.sum(total_goals > 2.5) / n_simulations
        over_15 = np.sum(total_goals > 1.5) / n_simulations
        
        # BTTS
        btts = np.sum((home_goals > 0) & (away_goals > 0)) / n_simulations
        
        # Average goals
        avg_total = np.mean(total_goals)
        
        prediction = max(hdw_probs, key=hdw_probs.get)
        
        return {
            'hdw': prediction,
            'hdw_confidence': hdw_probs[prediction],
            'hdw_probabilities': {k: round(v, 3) for k, v in hdw_probs.items()},
            'simulations': n_simulations,
            'avg_total_goals': round(avg_total, 2),
            'over_25_probability': round(over_25, 3),
            'over_15_probability': round(over_15, 3),
            'btts_probability': round(btts, 3),
            'reasoning': f"In {int(hdw_probs[prediction] * n_simulations):,} of {n_simulations:,} simulations, {prediction} was the outcome."
        }
