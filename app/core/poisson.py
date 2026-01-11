"""
Poisson Distribution Predictor
"""
import math
from typing import Dict


def poisson_pmf(k: int, mu: float) -> float:
    """Calculate Poisson probability mass function manually."""
    if mu <= 0:
        return 0.0 if k > 0 else 1.0
    return (math.exp(-mu) * (mu ** k)) / math.factorial(k)


class PoissonPredictor:
    """Statistical goal prediction using Poisson distribution"""
    
    def predict(self, home_attack: float, away_attack: float, 
                home_defense: float, away_defense: float,
                home_advantage: float = 1.3) -> Dict:
        """
        Predict match outcome using Poisson distribution.
        """
        # Expected goals
        home_expected = home_attack * away_defense * home_advantage
        away_expected = away_attack * home_defense
        
        # Clamp to reasonable values
        home_expected = max(0.5, min(4.0, home_expected))
        away_expected = max(0.5, min(4.0, away_expected))
        
        # Calculate probabilities for 0-5 goals
        max_goals = 6
        home_probs = [poisson_pmf(i, home_expected) for i in range(max_goals)]
        away_probs = [poisson_pmf(i, away_expected) for i in range(max_goals)]
        
        # HDW probabilities
        prob_home = prob_draw = prob_away = 0
        
        for h in range(max_goals):
            for a in range(max_goals):
                prob = home_probs[h] * away_probs[a]
                if h > a:
                    prob_home += prob
                elif h == a:
                    prob_draw += prob
                else:
                    prob_away += prob
        
        # Normalize
        total = prob_home + prob_draw + prob_away
        if total > 0:
            prob_home /= total
            prob_draw /= total
            prob_away /= total
        
        hdw_probs = {'H': prob_home, 'D': prob_draw, 'A': prob_away}
        prediction = max(hdw_probs, key=hdw_probs.get)
        
        # Over/Under
        total_expected = home_expected + away_expected
        prob_over_25 = 1 - sum([poisson_pmf(i, total_expected) for i in range(3)])
        prob_over_15 = 1 - sum([poisson_pmf(i, total_expected) for i in range(2)])
        
        # BTTS
        prob_btts = (1 - poisson_pmf(0, home_expected)) * (1 - poisson_pmf(0, away_expected))
        
        return {
            'hdw': prediction,
            'hdw_confidence': hdw_probs[prediction],
            'hdw_probabilities': hdw_probs,
            'expected_home_goals': round(home_expected, 2),
            'expected_away_goals': round(away_expected, 2),
            'over_25_probability': round(prob_over_25, 3),
            'over_15_probability': round(prob_over_15, 3),
            'btts_probability': round(prob_btts, 3),
            'reasoning': f"Statistical model projects {home_expected:.1f}-{away_expected:.1f} as expected scoreline."
        }
