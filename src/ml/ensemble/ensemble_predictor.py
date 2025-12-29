"""
Ensemble predictor combining ML and statistical models.

Weights and combines predictions from:
- ML models (XGBoost/LightGBM)
- Dixon-Coles Poisson model
"""
from typing import Dict, List, Any, Optional

from src.statistics.dixon_coles_model import DixonColesModel
from src.domain.services.prediction_service import PredictionService
from src.utils.logger import get_logger


class EnsemblePredictor:
    """
    Ensemble predictor combining ML and statistical models.
    
    Uses weighted averaging to combine:
    - ML predictions (data-driven, captures complex patterns)
    - Dixon-Coles (interpretable, theoretically grounded)
    """
    
    def __init__(
        self,
        ml_weight: float = 0.6,
        poisson_weight: float = 0.4,
        tier: str = "tier1",
    ):
        """
        Initialize ensemble predictor.
        
        Args:
            ml_weight: Weight for ML predictions (0-1)
            poisson_weight: Weight for Poisson predictions (0-1)
            tier: Model tier to use
        """
        self.logger = get_logger("EnsemblePredictor")
        
        # Weights should sum to 1
        total = ml_weight + poisson_weight
        self.ml_weight = ml_weight / total
        self.poisson_weight = poisson_weight / total
        
        self.tier = tier
        
        # Initialize models
        self.ml_service = PredictionService(tier=tier)
        self.poisson = DixonColesModel()
        
        self.is_initialized = False
    
    def initialize(self, historical_matches: List[Dict]) -> None:
        """
        Initialize both models with historical data.
        
        Args:
            historical_matches: List of historical match dicts
        """
        self.logger.info("Initializing ensemble predictor...")
        
        # Load ML models
        self.ml_service.load_models(tier=self.tier)
        
        # Fit Poisson model
        self.poisson.fit(historical_matches)
        
        self.is_initialized = True
        self.logger.info(
            f"Ensemble initialized: ML_weight={self.ml_weight:.2f}, "
            f"Poisson_weight={self.poisson_weight:.2f}"
        )
    
    def predict_over25(
        self,
        match: Dict,
        historical_matches: List[Dict],
    ) -> Dict[str, Any]:
        """
        Predict Over 2.5 goals with ensemble.
        
        Args:
            match: Match dict
            historical_matches: Historical data for context
            
        Returns:
            Ensemble prediction with both model outputs
        """
        home_team = match.get("home_team", "")
        away_team = match.get("away_team", "")
        
        # Get ML prediction
        ml_pred = self.ml_service.predict_match(match, historical_matches)
        ml_over25 = ml_pred.get("over25", {})
        ml_prob = ml_over25.get("probability", 0.5)
        
        # Get Poisson prediction
        poisson_prob = self.poisson.predict_over25_prob(home_team, away_team)
        
        # Ensemble (weighted average)
        ensemble_prob = (ml_prob * self.ml_weight) + (poisson_prob * self.poisson_weight)
        
        return {
            "prediction": "YES" if ensemble_prob > 0.5 else "NO",
            "probability": round(ensemble_prob, 4),
            "confidence": self._get_confidence(ensemble_prob),
            "ml_prediction": ml_over25,
            "poisson_prob": poisson_prob,
            "weights": {
                "ml": self.ml_weight,
                "poisson": self.poisson_weight,
            },
        }
    
    def predict_btts(
        self,
        match: Dict,
        historical_matches: List[Dict],
    ) -> Dict[str, Any]:
        """
        Predict Both Teams To Score with ensemble.
        
        Args:
            match: Match dict
            historical_matches: Historical data
            
        Returns:
            Ensemble prediction
        """
        home_team = match.get("home_team", "")
        away_team = match.get("away_team", "")
        
        # Get ML prediction
        ml_pred = self.ml_service.predict_match(match, historical_matches)
        ml_btts = ml_pred.get("btts", {})
        ml_prob = ml_btts.get("probability", 0.5)
        
        # Get Poisson prediction
        poisson_prob = self.poisson.predict_btts_prob(home_team, away_team)
        
        # Ensemble
        ensemble_prob = (ml_prob * self.ml_weight) + (poisson_prob * self.poisson_weight)
        
        return {
            "prediction": "YES" if ensemble_prob > 0.5 else "NO",
            "probability": round(ensemble_prob, 4),
            "confidence": self._get_confidence(ensemble_prob),
            "ml_prediction": ml_btts,
            "poisson_prob": poisson_prob,
        }
    
    def predict_result(
        self,
        match: Dict,
        historical_matches: List[Dict],
    ) -> Dict[str, Any]:
        """
        Predict match result (1X2) with ensemble.
        
        Args:
            match: Match dict
            historical_matches: Historical data
            
        Returns:
            Ensemble prediction
        """
        home_team = match.get("home_team", "")
        away_team = match.get("away_team", "")
        
        # Get ML prediction
        ml_pred = self.ml_service.predict_match(match, historical_matches)
        ml_result = ml_pred.get("result", {})
        ml_probs = ml_result.get("probabilities", {})
        ml_home = ml_probs.get("home_win", 0.4)
        ml_draw = ml_probs.get("draw", 0.27)
        ml_away = ml_probs.get("away_win", 0.33)
        
        # Get Poisson prediction
        poisson_result = self.poisson.predict_1x2(home_team, away_team)
        poisson_home = poisson_result.get("home_win", 0.4)
        poisson_draw = poisson_result.get("draw", 0.27)
        poisson_away = poisson_result.get("away_win", 0.33)
        
        # Ensemble
        ensemble_home = (ml_home * self.ml_weight) + (poisson_home * self.poisson_weight)
        ensemble_draw = (ml_draw * self.ml_weight) + (poisson_draw * self.poisson_weight)
        ensemble_away = (ml_away * self.ml_weight) + (poisson_away * self.poisson_weight)
        
        # Normalize
        total = ensemble_home + ensemble_draw + ensemble_away
        if total > 0:
            ensemble_home /= total
            ensemble_draw /= total
            ensemble_away /= total
        
        # Determine prediction
        max_prob = max(ensemble_home, ensemble_draw, ensemble_away)
        if ensemble_home == max_prob:
            prediction = "H"
        elif ensemble_away == max_prob:
            prediction = "A"
        else:
            prediction = "D"
        
        return {
            "prediction": prediction,
            "probabilities": {
                "home_win": round(ensemble_home, 4),
                "draw": round(ensemble_draw, 4),
                "away_win": round(ensemble_away, 4),
            },
            "confidence": self._get_confidence_1x2(max_prob),
            "ml_prediction": ml_result,
            "poisson_prediction": poisson_result,
        }
    
    def predict_match(
        self,
        match: Dict,
        historical_matches: List[Dict],
    ) -> Dict[str, Any]:
        """
        Generate all predictions for a match.
        
        Args:
            match: Match dict
            historical_matches: Historical data
            
        Returns:
            All ensemble predictions
        """
        home_team = match.get("home_team", "")
        away_team = match.get("away_team", "")
        
        # Get expected goals from Poisson
        home_xg, away_xg = self.poisson.get_expected_goals(home_team, away_team)
        
        return {
            "match": f"{home_team} vs {away_team}",
            "model": "ensemble",
            "expected_goals": {
                "home": round(home_xg, 2),
                "away": round(away_xg, 2),
                "total": round(home_xg + away_xg, 2),
            },
            "over25": self.predict_over25(match, historical_matches),
            "btts": self.predict_btts(match, historical_matches),
            "result": self.predict_result(match, historical_matches),
        }
    
    def _get_confidence(self, prob: float) -> str:
        """Get confidence level for binary prediction."""
        distance_from_50 = abs(prob - 0.5)
        
        if distance_from_50 >= 0.20:
            return "HIGH"
        elif distance_from_50 >= 0.10:
            return "MEDIUM"
        else:
            return "LOW"
    
    def _get_confidence_1x2(self, max_prob: float) -> str:
        """Get confidence level for 1X2 prediction."""
        if max_prob >= 0.50:
            return "HIGH"
        elif max_prob >= 0.40:
            return "MEDIUM"
        else:
            return "LOW"
