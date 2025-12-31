"""
Prediction service for making predictions on matches.

Main interface for using trained models to predict match outcomes.
"""
from datetime import datetime
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
        
        # Get feature names from trained model (if available) to ensure consistency
        if "over25" in self.models and self.models["over25"].feature_names:
            model_feature_names = self.models["over25"].feature_names
        elif "btts" in self.models and self.models["btts"].feature_names:
            model_feature_names = self.models["btts"].feature_names
        elif "result" in self.models and self.models["result"].feature_names:
            model_feature_names = self.models["result"].feature_names
        else:
            model_feature_names = list(flat_features.keys())
        
        # Build feature array in the EXACT order as training
        X = np.array([[flat_features.get(name, 0.0) for name in model_feature_names]])
        X = np.nan_to_num(X, nan=0.0)
        
        # Validate shape
        expected_features = len(model_feature_names)
        if X.shape[1] != expected_features:
            raise ValueError(f"Feature shape mismatch, expected: {expected_features}, got {X.shape[1]}")
        
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
        return {
            "prediction": "NO",
            "probability": 0.0,
            "confidence": "LOW",
            "fallback": True,
        }
    
    def _fallback_btts(self, features: Dict) -> Dict[str, Any]:
        """Fallback BTTS prediction."""
        return {
            "prediction": "NO",
            "probability": 0.0,
            "confidence": "LOW",
            "fallback": True,
        }
    
    def _fallback_result(self, features: Dict) -> Dict[str, Any]:
        """Fallback result prediction."""
        return {
            "prediction": "D",
            "probabilities": {"home_win": 0.0, "draw": 0.0, "away_win": 0.0},
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

    def analyze_matches_for_date(
        self,
        matches: List[Dict[str, Any]],
        historical_matches: List[Dict[str, Any]],
        dixon_coles_model: Any,
        match_stats_service: Any,
        h2h_service: Any
    ) -> List[Dict[str, Any]]:
        """
        Analyze a list of matches for a specific date.
        
        Orchestrates the entire analysis pipeline:
        1. ML Predictions
        2. Dixon-Coles Projections
        3. Match Stats (BTTS/Over25 qualification)
        4. H2H Statistics
        5. Formats response object
        
        Args:
            matches: List of match dictionaries (normalized)
            historical_matches: Historical match data
            comprehensive_service: Service for deeper analysis
            dixon_coles_model: Statistical model
            match_stats_service: Stats service
            h2h_service: H2H service
            
        Returns:
            List of fully analyzed match objects ready for API response
        """
        results = []
        
        for match in matches:
            match_date = match.get("match_date")
            home_team = match.get("home_team")
            away_team = match.get("away_team")
            league = match.get("league")
            
            # 1. Base ML Prediction
            prediction = self.predict_match(match, historical_matches)
            
            # 2. Dixon-Coles
            poisson = None
            if dixon_coles_model:
                try:
                    poisson = dixon_coles_model.predict_match(home_team, away_team)
                except Exception:
                    pass # Graceful fallback
            
            # 3. Match Stats & Qualification (BTTS/Over25)
            # Use 'as_of_date' logic if date is in past? 
            # For upcoming, we usually just pass None or today.
            stats = match_stats_service.calculate_match_stats(
                home_team, away_team, historical_matches, league=league
            )
            
            # 4. H2H
            h2h = h2h_service.get_h2h_stats(home_team, away_team, historical_matches)
            
            # 5. Odds (Safe extraction)
            odds = {
                "home": float(match.get("b365h") or 0.0),
                "draw": float(match.get("b365d") or 0.0),
                "away": float(match.get("b365a") or 0.0),
                "over25": float(match.get("b365_over25") or 0.0),
                "btts": float(match.get("btts_odds") or 0.0) # Strictly from source or 0
            }
            
            # 6. Build Consolidated Result
            analysis_result = {
                "match_id": match.get("match_id"),
                "home_team": home_team,
                "away_team": away_team,
                "date": match_date,
                "time": match.get("time"),
                "league": league,
                "odds": odds,
                "predictions": prediction, # ML Output
                "poisson_distribution": poisson,
                "team_stats": stats,
                "h2h": h2h,
                "average": { # Calculated fields
                    "home_goal_avg": stats.get("home_team", {}).get("goals_scored_avg", 0.0),
                    "away_goal_avg": stats.get("away_team", {}).get("goals_scored_avg", 0.0)
                }
            }
            results.append(analysis_result)
            
        return results
