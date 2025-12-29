"""
Odds filter service for value bet detection.

Filters matches based on odds ranges and calculates value
indicators for risk/reward optimization.
"""
from typing import Dict, List, Any, Optional

from src.domain.services.base_service import BaseService


class OddsFilterService(BaseService):
    """
    Service for filtering matches by odds and detecting value bets.
    
    Helps identify:
    - Matches with odds in optimal betting ranges
    - Value bets where prediction prob > implied prob
    - Low-risk/high-reward opportunities
    """
    
    # Default optimal odds ranges (can be customized)
    DEFAULT_RANGES = {
        "1x2": {"min": 1.70, "max": 2.50},  # Result betting
        "over25": {"min": 1.70, "max": 2.30},  # Over 2.5
        "btts": {"min": 1.70, "max": 2.30},  # Both teams to score
    }
    
    def __init__(self, odds_ranges: Optional[Dict] = None):
        """
        Initialize odds filter service.
        
        Args:
            odds_ranges: Optional custom odds ranges per market
        """
        super().__init__()
        
        self.odds_ranges = odds_ranges or self.DEFAULT_RANGES
    
    def filter_by_odds_range(
        self,
        matches: List[Dict],
        market: str,
        min_odds: Optional[float] = None,
        max_odds: Optional[float] = None,
    ) -> List[Dict]:
        """
        Filter matches to those with odds in specified range.
        
        Args:
            matches: List of match dicts with odds
            market: Market type ('1x2', 'over25', 'btts')
            min_odds: Minimum odds (default from config)
            max_odds: Maximum odds (default from config)
            
        Returns:
            Filtered list of matches
        """
        range_config = self.odds_ranges.get(market, {})
        min_odds = min_odds or range_config.get("min", 1.5)
        max_odds = max_odds or range_config.get("max", 3.0)
        
        # Map market to odds column
        odds_column_map = {
            "1x2_home": "b365h",
            "1x2_draw": "b365d",
            "1x2_away": "b365a",
            "over25": "b365_over25",
            "btts": "b365_over25",  # Use over25 as proxy if btts not available
        }
        
        # For 1x2, we check the most likely outcome
        if market == "1x2":
            return self._filter_1x2(matches, min_odds, max_odds)
        
        odds_col = odds_column_map.get(market, "b365_over25")
        
        filtered = []
        for match in matches:
            odds = match.get(odds_col) or match.get(f"{market}_odds")
            
            if odds is None:
                continue
            
            try:
                odds = float(odds)
                if min_odds <= odds <= max_odds:
                    filtered.append(match)
            except (ValueError, TypeError):
                continue
        
        self.logger.info(
            f"Filtered {len(matches)} -> {len(filtered)} matches "
            f"(market={market}, odds={min_odds}-{max_odds})"
        )
        
        return filtered
    
    def _filter_1x2(
        self,
        matches: List[Dict],
        min_odds: float,
        max_odds: float,
    ) -> List[Dict]:
        """Filter 1X2 market by checking all three outcomes."""
        filtered = []
        
        for match in matches:
            home_odds = match.get("b365h")
            draw_odds = match.get("b365d")
            away_odds = match.get("b365a")
            
            # Check if any outcome is in range
            try:
                if home_odds and min_odds <= float(home_odds) <= max_odds:
                    filtered.append(match)
                elif away_odds and min_odds <= float(away_odds) <= max_odds:
                    filtered.append(match)
                elif draw_odds and min_odds <= float(draw_odds) <= max_odds:
                    filtered.append(match)
            except (ValueError, TypeError):
                continue
        
        return filtered
    
    def calculate_value(
        self,
        prediction_prob: float,
        bookmaker_odds: float,
    ) -> float:
        """
        Calculate value of a bet.
        
        Value = (prediction_prob * odds) - 1
        Positive value = profitable long-term bet
        
        Args:
            prediction_prob: Our predicted probability (0-1)
            bookmaker_odds: Bookmaker decimal odds
            
        Returns:
            Value indicator (positive = value bet)
        """
        if bookmaker_odds <= 1:
            return -1.0
        
        expected_return = prediction_prob * bookmaker_odds
        return expected_return - 1.0
    
    def calculate_edge(
        self,
        prediction_prob: float,
        bookmaker_odds: float,
    ) -> float:
        """
        Calculate edge (difference from implied prob).
        
        Edge = prediction_prob - implied_prob
        
        Args:
            prediction_prob: Our predicted probability (0-1)
            bookmaker_odds: Bookmaker decimal odds
            
        Returns:
            Edge (positive = we think more likely than bookmaker)
        """
        if bookmaker_odds <= 1:
            return 0.0
        
        implied_prob = 1.0 / bookmaker_odds
        return prediction_prob - implied_prob
    
    def get_value_bets(
        self,
        matches: List[Dict],
        predictions: List[Dict],
        market: str = "over25",
        min_value: float = 0.05,
    ) -> List[Dict]:
        """
        Get value bets from predictions.
        
        Args:
            matches: List of match dicts with odds
            predictions: List of prediction dicts
            market: Market to check ('over25', 'btts', 'result')
            min_value: Minimum value threshold (default 5%)
            
        Returns:
            List of matches with positive value
        """
        value_bets = []
        
        for match, pred in zip(matches, predictions):
            if market == "over25":
                odds = match.get("b365_over25") or match.get("over25_odds")
                prob = pred.get("over25", {}).get("probability")
            elif market == "btts":
                odds = match.get("b365_btts") or match.get("btts_odds")
                prob = pred.get("btts", {}).get("probability")
            elif market == "result":
                # For result, take the predicted outcome
                result_pred = pred.get("result", {})
                predicted = result_pred.get("prediction", "H")
                probs = result_pred.get("probabilities", {})
                
                if predicted == "H":
                    odds = match.get("b365h")
                    prob = probs.get("home_win")
                elif predicted == "A":
                    odds = match.get("b365a")
                    prob = probs.get("away_win")
                else:
                    odds = match.get("b365d")
                    prob = probs.get("draw")
            else:
                continue
            
            if odds is None or prob is None:
                continue
            
            try:
                odds = float(odds)
                prob = float(prob)
            except (ValueError, TypeError):
                continue
            
            value = self.calculate_value(prob, odds)
            edge = self.calculate_edge(prob, odds)
            
            if value >= min_value:
                value_bets.append({
                    **match,
                    "prediction": pred,
                    "market": market,
                    "odds": odds,
                    "probability": prob,
                    "value": value,
                    "edge": edge,
                })
        
        # Sort by value descending
        value_bets.sort(key=lambda x: x["value"], reverse=True)
        
        self.logger.info(
            f"Found {len(value_bets)} value bets with >= {min_value:.1%} value"
        )
        
        return value_bets
    
    def is_low_odds(
        self,
        odds: float,
        threshold: float = 1.50,
    ) -> bool:
        """
        Check if odds are too low (heavy favorite).
        
        Low odds = poor risk/reward ratio.
        
        Args:
            odds: Decimal odds
            threshold: Maximum odds to consider "low"
            
        Returns:
            True if odds are below threshold
        """
        return odds < threshold
    
    def get_odds_quality(self, odds: float) -> str:
        """
        Get qualitative assessment of odds.
        
        Args:
            odds: Decimal odds
            
        Returns:
            Quality string ('poor', 'fair', 'good', 'excellent')
        """
        if odds < 1.50:
            return "poor"  # Too short, bad value
        elif odds < 1.80:
            return "fair"  # Reasonable
        elif odds <= 2.50:
            return "good"  # Optimal range
        elif odds <= 4.00:
            return "fair"  # Higher risk
        else:
            return "poor"  # Too long, unlikely
