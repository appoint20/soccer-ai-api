"""
Match Confidence Calculator.

Calculates aggregated reliability score (0-100) for AI ranking.
"""
from dataclasses import dataclass
from typing import Dict, Any, Optional

from src.domain.services.calculators.team_form_calculator import TeamFormStats
from src.domain.services.calculators.h2h_stats_calculator import H2HStats
from src.domain.services.calculators.poisson_goal_calculator import PoissonProbabilities
from src.domain.services.calculators.monte_carlo_uncertainty_adjuster import MonteCarloResults
from src.utils.logger import get_logger


# League data quality weights (based on historical data density)
LEAGUE_QUALITY = {
    "E0": 1.0,   # Premier League - best data
    "E1": 0.95,  # Championship
    "E2": 0.90,  # League One
    "E3": 0.85,  # League Two
    "D1": 0.95,  # Bundesliga
    "SP1": 0.95, # La Liga
    "I1": 0.95,  # Serie A
    "I2": 0.90,  # Serie B
    "F1": 0.95,  # Ligue 1
    "F2": 0.90,  # Ligue 2
}


class MatchConfidenceCalculator:
    """
    Calculate overall confidence index (0-100) for a match analysis.
    
    Factors:
    - Sample sizes (team form, H2H)
    - Agreement between Poisson and Monte Carlo
    - H2H reliability
    - League data quality
    - Market entropy (spread of probabilities)
    """
    
    def __init__(self):
        self.logger = get_logger("MatchConfidenceCalculator")
    
    def calculate_confidence_index(
        self,
        home_stats: TeamFormStats,
        away_stats: TeamFormStats,
        h2h_stats: H2HStats,
        poisson_probs: PoissonProbabilities,
        mc_results: MonteCarloResults,
        league_code: str,
    ) -> int:
        """
        Calculate overall confidence score (0-100).
        
        Higher score = more reliable prediction data.
        Used for AI sorting and bet selection prioritization.
        """
        scores = []
        weights = []
        
        # 1. Sample Size Score (25% weight)
        sample_score = self._calculate_sample_score(home_stats, away_stats)
        scores.append(sample_score)
        weights.append(0.25)
        
        # 2. H2H Reliability Score (15% weight)
        h2h_score = h2h_stats.h2h_reliability * 100
        scores.append(h2h_score)
        weights.append(0.15)
        
        # 3. Poisson-MC Agreement Score (25% weight)
        agreement_score = self._calculate_agreement_score(poisson_probs, mc_results)
        scores.append(agreement_score)
        weights.append(0.25)
        
        # 4. League Quality Score (15% weight)
        league_score = LEAGUE_QUALITY.get(league_code, 0.8) * 100
        scores.append(league_score)
        weights.append(0.15)
        
        # 5. Market Entropy Score (20% weight)
        # Lower entropy = more decisive prediction = higher confidence
        entropy_score = self._calculate_entropy_score(poisson_probs)
        scores.append(entropy_score)
        weights.append(0.20)
        
        # Weighted average
        total_weight = sum(weights)
        confidence = sum(s * w for s, w in zip(scores, weights)) / total_weight
        
        return int(round(confidence))
    
    def _calculate_sample_score(
        self,
        home_stats: TeamFormStats,
        away_stats: TeamFormStats,
    ) -> float:
        """Score based on team form sample sizes."""
        # Ideal: 5 matches each with good effective sample size
        home_sample = min(5, home_stats.sample_size)
        away_sample = min(5, away_stats.sample_size)
        
        # Also consider effective sample size (weighted)
        home_effective = min(4.0, home_stats.effective_sample_size)
        away_effective = min(4.0, away_stats.effective_sample_size)
        
        raw_score = (home_sample + away_sample) / 10  # 0-1
        effective_bonus = (home_effective + away_effective) / 8  # 0-1
        
        return (raw_score * 0.6 + effective_bonus * 0.4) * 100
    
    def _calculate_agreement_score(
        self,
        poisson_probs: PoissonProbabilities,
        mc_results: MonteCarloResults,
    ) -> float:
        """Score based on agreement between Poisson and Monte Carlo."""
        # Compare key markets
        markets = [
            (poisson_probs.over_25, mc_results.over_25.adjusted_probability),
            (poisson_probs.btts, mc_results.btts.adjusted_probability),
            (poisson_probs.home_win, mc_results.home_win.adjusted_probability),
            (poisson_probs.draw, mc_results.draw.adjusted_probability),
        ]
        
        total_deviation = 0.0
        for poisson_prob, mc_prob in markets:
            deviation = abs(poisson_prob - mc_prob)
            total_deviation += deviation
        
        avg_deviation = total_deviation / len(markets)
        
        # Lower deviation = higher agreement = higher score
        # Max deviation (after capping) is ~0.07, so scale accordingly
        agreement = 1.0 - min(1.0, avg_deviation / 0.15)
        
        return agreement * 100
    
    def _calculate_entropy_score(
        self,
        poisson_probs: PoissonProbabilities,
    ) -> float:
        """
        Score based on market entropy.
        
        Low entropy = one outcome is clearly favored = higher confidence.
        High entropy (all outcomes ~equal) = uncertain = lower confidence.
        """
        import math
        
        # 1X2 entropy
        probs_1x2 = [
            max(0.01, poisson_probs.home_win),
            max(0.01, poisson_probs.draw),
            max(0.01, poisson_probs.away_win),
        ]
        
        # Normalize
        total = sum(probs_1x2)
        probs_1x2 = [p / total for p in probs_1x2]
        
        # Shannon entropy
        entropy = -sum(p * math.log2(p) for p in probs_1x2)
        
        # Max entropy for 3 outcomes is log2(3) ≈ 1.585
        max_entropy = math.log2(3)
        normalized_entropy = entropy / max_entropy  # 0-1
        
        # Lower entropy = higher confidence
        entropy_score = (1.0 - normalized_entropy) * 100
        
        return entropy_score
