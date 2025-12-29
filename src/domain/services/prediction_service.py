"""
Prediction service for making predictions on matches.

Main interface for using trained models to predict match outcomes.
"""
from datetime import date, datetime
from pathlib import Path
from typing import Any, Dict, List, Optional, Union
import numpy as np

from src.domain.services.base_service import BaseService
from src.domain.services.feature_engineering_service import FeatureEngineeringService
from src.ml.models.over25_model import Over25Model
from src.ml.models.btts_model import BTTSModel
from src.ml.models.result_model import ResultModel
from src.data.cache.cache_manager import CacheManager


class PredictionService(BaseService):
    """
    Main service for making predictions on soccer matches.
    
    Uses trained ML models to predict:
    - Over 2.5 goals
    - Both teams to score (BTTS)
    - Match result (H/D/A)
    """
    
    def __init__(
        self,
        feature_service: Optional[FeatureEngineeringService] = None,
        models_dir: Union[str, Path] = "models",
        cache_manager: Optional[CacheManager] = None,
        tier: str = "tier1",
    ):
        """
        Initialize prediction service.
        
        Args:
            feature_service: Feature engineering service instance
            models_dir: Directory containing trained models
            cache_manager: Optional cache manager
            tier: Model tier to use ('tier1', 'tier2', 'tier3')
        """
        super().__init__(cache_manager)
        
        self.feature_service = feature_service or FeatureEngineeringService()
        self.models_dir = Path(models_dir)
        self.tier = tier
        
        self.models: Dict[str, Any] = {}
        self._models_loaded = False
    
    def load_models(self, tier: Optional[str] = None) -> bool:
        """
        Load trained models for prediction.
        
        Args:
            tier: Optional tier override
            
        Returns:
            True if all models loaded successfully
        """
        tier = tier or self.tier
        tier_dir = self.models_dir / tier
        
        success = True
        
        # Load Over 2.5 model
        over25_path = tier_dir / "over25" / "model.pkl"
        if over25_path.exists():
            try:
                self.models["over25"] = Over25Model()
                self.models["over25"].load(over25_path)
                self.logger.info(f"Loaded over25 model from {over25_path}")
            except Exception as e:
                self.logger.warning(f"Failed to load over25 model: {e}")
                success = False
        else:
            self.logger.warning(f"Over25 model not found at {over25_path}")
            success = False
        
        # Load BTTS model
        btts_path = tier_dir / "btts" / "model.pkl"
        if btts_path.exists():
            try:
                self.models["btts"] = BTTSModel()
                self.models["btts"].load(btts_path)
                self.logger.info(f"Loaded btts model from {btts_path}")
            except Exception as e:
                self.logger.warning(f"Failed to load btts model: {e}")
                success = False
        else:
            self.logger.warning(f"BTTS model not found at {btts_path}")
            success = False
        
        # Load Result model
        result_path = tier_dir / "result" / "model.pkl"
        if result_path.exists():
            try:
                self.models["result"] = ResultModel()
                self.models["result"].load(result_path)
                self.logger.info(f"Loaded result model from {result_path}")
            except Exception as e:
                self.logger.warning(f"Failed to load result model: {e}")
                success = False
        else:
            self.logger.warning(f"Result model not found at {result_path}")
            success = False
        
        self._models_loaded = len(self.models) > 0
        return success
    
    def predict_match(
        self,
        match_info: Dict[str, Any],
        historical_matches: List,
    ) -> Dict[str, Any]:
        """
        Predict outcomes for a single match.
        
        Args:
            match_info: Dict with home_team, away_team, date, league
            historical_matches: Historical matches for feature generation
            
        Returns:
            Dict with all predictions
        """
        # Generate features
        features = self.feature_service.generate_features_for_match(
            match_info, historical_matches
        )
        
        # Flatten features for ML
        flat_features = self.feature_service.flatten_features(features)
        
        # Convert to numpy array
        feature_names = list(flat_features.keys())
        X = np.array([[flat_features.get(name, 0.0) for name in feature_names]])
        X = np.nan_to_num(X, nan=0.0)
        
        predictions = {
            "match_id": features.get("match_id"),
            "home_team": match_info.get("home_team"),
            "away_team": match_info.get("away_team"),
            "date": str(match_info.get("match_date", ""))[:10],
            "league": match_info.get("league"),
            "timestamp": datetime.now().isoformat(),
        }
        
        # Over 2.5 prediction
        if "over25" in self.models:
            over25_model = self.models["over25"]
            over25_proba = over25_model.predict_proba(X)[0]
            over25_pred = over25_model.predict_with_confidence(X)[0]
            
            predictions["over25"] = {
                "prediction": over25_pred["prediction"],
                "probability": round(over25_proba, 3),
                "confidence": over25_pred["confidence"],
            }
        else:
            predictions["over25"] = self._fallback_over25(features)
        
        # BTTS prediction
        if "btts" in self.models:
            btts_model = self.models["btts"]
            btts_proba = btts_model.predict_proba(X)[0]
            btts_pred = btts_model.predict_with_confidence(X)[0]
            
            predictions["btts"] = {
                "prediction": btts_pred["prediction"],
                "probability": round(btts_proba, 3),
                "confidence": btts_pred["confidence"],
            }
        else:
            predictions["btts"] = self._fallback_btts(features)
        
        # Result prediction
        if "result" in self.models:
            result_model = self.models["result"]
            result_pred = result_model.predict_with_confidence(X)[0]
            
            predictions["result"] = {
                "prediction": result_pred["prediction"],
                "probabilities": result_pred["probabilities"],
                "confidence": result_pred["confidence"],
            }
        else:
            predictions["result"] = self._fallback_result(features)
        
        return predictions
    
    def predict_matches(
        self,
        matches: List[Dict[str, Any]],
        historical_matches: List,
    ) -> List[Dict[str, Any]]:
        """
        Predict outcomes for multiple matches.
        
        Args:
            matches: List of match dicts
            historical_matches: Historical matches for features
            
        Returns:
            List of prediction dicts
        """
        predictions = []
        
        for match in matches:
            try:
                pred = self.predict_match(match, historical_matches)
                predictions.append(pred)
            except Exception as e:
                self.logger.error(f"Failed to predict match: {e}")
                predictions.append({
                    "match_id": f"{match.get('home_team')}_vs_{match.get('away_team')}",
                    "error": str(e),
                })
        
        return predictions
    
    def _fallback_over25(self, features: Dict) -> Dict[str, Any]:
        """Fallback Over 2.5 prediction using feature averages."""
        home_features = features.get("home_features", {})
        away_features = features.get("away_features", {})
        h2h = features.get("h2h_features", {})
        
        # Simple average of over25 rates
        rates = [
            home_features.get("over25_rate_season", 0.5),
            away_features.get("over25_rate_season", 0.5),
            h2h.get("over25_rate", 0.5),
        ]
        
        avg_prob = sum(rates) / len(rates)
        
        return {
            "prediction": "YES" if avg_prob > 0.5 else "NO",
            "probability": round(avg_prob, 3),
            "confidence": "LOW",
            "fallback": True,
        }
    
    def _fallback_btts(self, features: Dict) -> Dict[str, Any]:
        """Fallback BTTS prediction using feature averages."""
        home_features = features.get("home_features", {})
        away_features = features.get("away_features", {})
        h2h = features.get("h2h_features", {})
        
        rates = [
            home_features.get("btts_rate_season", 0.5),
            away_features.get("btts_rate_season", 0.5),
            h2h.get("btts_rate", 0.5),
        ]
        
        avg_prob = sum(rates) / len(rates)
        
        return {
            "prediction": "YES" if avg_prob > 0.5 else "NO",
            "probability": round(avg_prob, 3),
            "confidence": "LOW",
            "fallback": True,
        }
    
    def _fallback_result(self, features: Dict) -> Dict[str, Any]:
        """Fallback result prediction using feature averages."""
        home_features = features.get("home_features", {})
        away_features = features.get("away_features", {})
        
        home_win_rate = home_features.get("win_rate_venue", 0.45)
        away_win_rate = away_features.get("win_rate_venue", 0.30)
        
        # Normalize
        total = home_win_rate + away_win_rate + 0.25  # 0.25 for draw
        home_prob = home_win_rate / total
        away_prob = away_win_rate / total
        draw_prob = 0.25 / total
        
        probs = {"home_win": home_prob, "draw": draw_prob, "away_win": away_prob}
        prediction = max(probs, key=probs.get)
        
        return {
            "prediction": {"home_win": "H", "draw": "D", "away_win": "A"}[prediction],
            "probabilities": {k: round(v, 3) for k, v in probs.items()},
            "confidence": "LOW",
            "fallback": True,
        }
    
    def get_model_info(self) -> Dict[str, Any]:
        """
        Get information about loaded models.
        
        Returns:
            Dict with model info
        """
        info = {
            "tier": self.tier,
            "models_loaded": self._models_loaded,
            "models": {},
        }
        
        for name, model in self.models.items():
            info["models"][name] = model.get_info()
        
        return info
