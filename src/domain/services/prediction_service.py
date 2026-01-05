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
from src.statistics.monte_carlo import MonteCarloPredictor
from src.domain.services.derby_service import DerbyService


class PredictionService(BaseService):
    """
    Main service for making predictions on soccer matches.
    
    Uses trained ML models, Monte Carlo simulations, and heuristics to predict:
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

        # Additional components for comprehensive analysis
        self.derby_service = DerbyService()
        self.monte_carlo = MonteCarloPredictor(n_simulations=5000)
    
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
                "probability": float(round(over25_proba, 3)),
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
                "probability": float(round(btts_proba, 3)),
                "confidence": btts_pred["confidence"],
            }
        else:
            predictions["btts"] = self._fallback_btts(features)
        
        # Result prediction
        if "result" in self.models:
            result_model = self.models["result"]
            result_pred = result_model.predict_with_confidence(X)[0]
            
            # Ensure probabilities are floats
            probs = result_pred["probabilities"]
            clean_probs = {k: float(v) for k, v in probs.items()}
            
            predictions["result"] = {
                "prediction": result_pred["prediction"],
                "probabilities": clean_probs,
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

    def _get_monte_carlo_prediction(self, home_team: str, away_team: str) -> Dict[str, Any]:
        """Get Monte Carlo predictions."""
        try:
            pred = self.monte_carlo.predict(home_team, away_team)
            draw_analysis = pred.get("draw_analysis", {})
            
            return {
                "over25": {
                    "prediction": "YES" if pred.get("over25", 0) > 0.5 else "NO",
                    "probability": round(pred.get("over25", 0.5), 4),
                },
                "btts": {
                    "prediction": "YES" if pred.get("btts", 0) > 0.5 else "NO",
                    "probability": round(pred.get("btts", 0.5), 4),
                },
                "result": {
                    "prediction": pred.get("prediction", "D"),
                    "probabilities": pred.get("probabilities", {}),
                    "confidence": self._prob_to_confidence(
                        max(pred.get("probabilities", {}).values()) if pred.get("probabilities") else 0.33
                    ),
                },
                "draw_likelihood": {
                    "is_draw_likely": draw_analysis.get("is_draw_likely", False),
                    "draw_score": draw_analysis.get("draw_score", 0),
                    "signals": draw_analysis.get("signals", []),
                },
                "top_scorelines": pred.get("top_scorelines", []),
            }
        except Exception as e:
            return {"error": str(e)}

    def _combine_predictions(self, ml: Dict, dc: Dict, mc: Dict) -> Dict[str, Any]:
        """Combine predictions into ensemble: ML 30%, DC 40%, MC 30%."""
        ML_W, DC_W, MC_W = 0.30, 0.40, 0.30
        
        # Over 2.5
        ml_o25 = float(ml.get("over25", {}).get("probability", 0.5)) if "error" not in ml else 0.5
        dc_o25 = float(dc.get("over25", {}).get("probability", 0.5)) if "error" not in dc else 0.5
        mc_o25 = float(mc.get("over25", {}).get("probability", 0.5)) if "error" not in mc else 0.5
        
        ens_o25 = ml_o25 * ML_W + dc_o25 * DC_W + mc_o25 * MC_W
        
        # BTTS
        ml_btts = float(ml.get("btts", {}).get("probability", 0.5)) if "error" not in ml else 0.5
        dc_btts = float(dc.get("btts", {}).get("probability", 0.5)) if "error" not in dc else 0.5
        mc_btts = float(mc.get("btts", {}).get("probability", 0.5)) if "error" not in mc else 0.5
        
        ens_btts = ml_btts * ML_W + dc_btts * DC_W + mc_btts * MC_W
        
        # Result (1X2)
        ml_probs = ml.get("result", {}).get("probabilities", {}) if "error" not in ml else {}
        dc_probs = dc.get("result", {}).get("probabilities", {}) if "error" not in dc else {}
        mc_probs = mc.get("result", {}).get("probabilities", {}) if "error" not in mc else {}
        
        ens_home = float(ml_probs.get("home_win", 0.33)) * ML_W + float(dc_probs.get("home_win", 0.33)) * DC_W + float(mc_probs.get("home_win", 0.33)) * MC_W
        ens_draw = float(ml_probs.get("draw", 0.33)) * ML_W + float(dc_probs.get("draw", 0.33)) * DC_W + float(mc_probs.get("draw", 0.33)) * MC_W
        ens_away = float(ml_probs.get("away_win", 0.33)) * ML_W + float(dc_probs.get("away_win", 0.33)) * DC_W + float(mc_probs.get("away_win", 0.33)) * MC_W
        
        # Normalize
        total = ens_home + ens_draw + ens_away
        if total > 0:
            ens_home /= total
            ens_draw /= total
            ens_away /= total
            
        max_prob = max(ens_home, ens_draw, ens_away)
        if ens_home == max_prob: res_pred = "H"
        elif ens_away == max_prob: res_pred = "A"
        else: res_pred = "D"
        
        # Agreement count
        ml_p = ml.get("result", {}).get("prediction", "D") if "error" not in ml else None
        dc_p = dc.get("result", {}).get("prediction", "D") if "error" not in dc else None
        mc_p = mc.get("result", {}).get("prediction", "D") if "error" not in mc else None
        agreement = sum(1 for p in [ml_p, dc_p, mc_p] if p == res_pred)
        
        return {
            "over25": {
                "prediction": "YES" if ens_o25 > 0.5 else "NO",
                "probability": float(round(ens_o25, 4)),
                "confidence": self._prob_to_confidence_binary(ens_o25),
            },
            "btts": {
                "prediction": "YES" if ens_btts > 0.5 else "NO",
                "probability": float(round(ens_btts, 4)),
                "confidence": self._prob_to_confidence_binary(ens_btts),
            },
            "result": {
                "prediction": res_pred,
                "probabilities": {
                    "home_win": float(round(ens_home, 4)),
                    "draw": float(round(ens_draw, 4)),
                    "away_win": float(round(ens_away, 4))
                },
                "confidence": self._prob_to_confidence(max_prob),
            },
            "model_agreement": f"{agreement}/3",
            "weights": {"ml": ML_W, "dixon_coles": DC_W, "monte_carlo": MC_W},
        }

    # _generate_reasons removed

    def _prob_to_confidence(self, prob: float) -> str:
        if prob >= 0.60: return "HIGH"
        elif prob >= 0.45: return "MEDIUM"
        return "LOW"

    def _prob_to_confidence_binary(self, prob: float) -> str:
        if abs(prob - 0.5) >= 0.20: return "HIGH"
        elif abs(prob - 0.5) >= 0.10: return "MEDIUM"
        return "LOW"
    
    def _get_confidence_summary(self, ensemble: Dict) -> Dict:
        """Get summary of confidence levels."""
        return {
            "over25": ensemble.get("over25", {}).get("confidence", "LOW"),
            "btts": ensemble.get("btts", {}).get("confidence", "LOW"),
            "result": ensemble.get("result", {}).get("confidence", "LOW"),
        }

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
        3. Monte Carlo Simulations
        4. Match Stats (BTTS/Over25 qualification)
        5. H2H Statistics
        6. Ensemble Combination (Weighted Average)
        7. Reason Generation
        
        Args:
            matches: List of match dictionaries (normalized)
            historical_matches: Historical match data
            dixon_coles_model: Statistical model
            match_stats_service: Stats service
            h2h_service: H2H service
            
        Returns:
            List of fully analyzed match objects ready for API response
        """
        results = []
        
        # Ensure services are linked if passed in (backward compatibility)
        # Ideally these are injected in init, but for now we use what's passed or what's initialized
        
        # counters for logging
        stats_counts = {
            "total": len(matches),
            "with_history": 0,
            "qualified_over25": 0,
            "qualified_btts": 0
        }

        for match in matches:
            match_date = match.get("match_date")
            home_team = match.get("home_team")
            away_team = match.get("away_team")
            league = match.get("league")
            
            # 1. Base ML Prediction
            prediction = self.predict_match(match, historical_matches)
            
            # 2. Dixon-Coles Prediction (via model or passed instance if needed, but we have _get_dixon_coles_prediction logic too)
            # To avoid duplication, let's use the passed model for calculation
            poisson = None
            dc_prediction = {} # Format for ensemble
            
            if dixon_coles_model:
                try:
                    # Get raw poisson stats for UI
                    poisson = dixon_coles_model.predict_match(home_team, away_team)
                    
                    # Round expected goals for UI readability
                    if poisson:
                        if "expected_home_goals" in poisson:
                            poisson["expected_home_goals"] = round(float(poisson["expected_home_goals"]), 2)
                        if "expected_away_goals" in poisson:
                             poisson["expected_away_goals"] = round(float(poisson["expected_away_goals"]), 2)
                    
                    # Compute standardized prediction dict for ensemble
                    xg = dixon_coles_model.get_expected_goals(home_team, away_team)
                    res_probs = dixon_coles_model.predict_1x2(home_team, away_team)
                    o25 = dixon_coles_model.predict_over25_prob(home_team, away_team)
                    btts_prob = dixon_coles_model.predict_btts_prob(home_team, away_team)
                    
                    # Determine result prediction
                    if res_probs:
                        max_p = max(res_probs.values()) 
                        res_k = max(res_probs, key=res_probs.get)
                        r_code = "H" if res_k == "home_win" else "A" if res_k == "away_win" else "D"
                    else:
                        max_p = 0
                        r_code = "D"

                    dc_prediction = {
                         "over25": {"prediction": "YES" if o25 > 0.5 else "NO", "probability": round(o25, 4)},
                         "btts": {"prediction": "YES" if btts_prob > 0.5 else "NO", "probability": round(btts_prob, 4)},
                         "result": {
                             "prediction": r_code, 
                             "probabilities": res_probs, 
                             "confidence": self._prob_to_confidence(max_p)
                         }
                    }
                except Exception as e:
                    self.logger.error(f"Dixon-Coles failed for {home_team} vs {away_team}: {e}")
                    dc_prediction = {"error": "Failed"}
            
            # 3. Monte Carlo Prediction
            mc_prediction = self._get_monte_carlo_prediction(home_team, away_team)
            
            # 4. Ensemble
            ensemble = self._combine_predictions(prediction, dc_prediction, mc_prediction)
            
            # 5. Match Stats & Qualification (Aggregate)
            # Get H2H matches first
            h2h_matches_list = h2h_service.extract_h2h_matches(home_team, away_team, historical_matches)
            
            aggregate_stats = match_stats_service.calculate_aggregate_stats(
                home_team, away_team, historical_matches, h2h_matches_list
            )
            
            # 6. Extract Odds
            odds = {
                "home": float(match.get("b365h") or 0.0),
                "draw": float(match.get("b365d") or 0.0),
                "away": float(match.get("b365a") or 0.0),
                "over25": float(match.get("b365_over25") or match.get("over25_odds") or 0.0),
                "btts": float(match.get("b365_btts") or match.get("btts_odds") or 0.0)
            }
            
            # 7. Inject Predictions (Ensemble) & Poisson
            
            # Over 2.5
            if "over25" in aggregate_stats:
                ens_o25 = ensemble.get("over25", {})
                aggregate_stats["over25"]["prediction"] = ens_o25.get("prediction", "N/A")
                aggregate_stats["over25"]["probability"] = round(float(ens_o25.get("probability", 0.0)), 3)
                if poisson:
                     aggregate_stats["over25"]["poisson_probability"] = round(poisson.get("over25", 0.0) * 100, 1)

            # BTTS
            if "btts" in aggregate_stats:
                ens_btts = ensemble.get("btts", {})
                aggregate_stats["btts"]["prediction"] = ens_btts.get("prediction", "N/A")
                aggregate_stats["btts"]["probability"] = round(float(ens_btts.get("probability", 0.0)), 3)
                if poisson:
                     aggregate_stats["btts"]["poisson_probability"] = round(poisson.get("btts", 0.0) * 100, 1)
            
            # Result
            if "result" in aggregate_stats:
                ens_res = ensemble.get("result", {})
                pred_outcome = ens_res.get("prediction", "N/A")
                aggregate_stats["result"]["prediction"] = pred_outcome
                
                # Get probability of predicted outcome
                res_probs = ens_res.get("probabilities", {})
                if pred_outcome == "H":
                    prob = res_probs.get("home_win", 0.0)
                elif pred_outcome == "A":
                    prob = res_probs.get("away_win", 0.0)
                else:
                    prob = res_probs.get("draw", 0.0)
                
                aggregate_stats["result"]["probability"] = round(float(prob), 3)
                
                if poisson:
                     # Home Win Probability as requested? Or just generic poisson probs?
                     # Let's use Home Win Probability for consistency with user request roughly?
                     # Actually, for "Result", poisson probability is ambiguous (H, D, or A).
                     # Let's map it to Home Win % for now as it's a common metric, OR better, map it to the PREDICTED outcome's poisson prob?
                     # User asked "beside the average percentage calc add poisson distributions".
                     # Let's stick to Home Win % as a reference if no specific request.
                     aggregate_stats["result"]["poisson_probability"] = round(poisson.get("home_win", 0.0) * 100, 1)

            # Construct Final Object matching AggregateMatchAnalysis
            match_analysis = {
                "match_id": match.get("match_id"),
                "home_team": home_team,
                "away_team": away_team,
                "date": match_date,
                "time": match.get("time"),
                "league": league,
                "odds": odds,
                "analysis": aggregate_stats,
                "ai_insight": None # Will be populated later
            }
            
            results.append(match_analysis)
            
        # Log Summary
        self.logger.info("="*50)
        if results:
             self.logger.info(f"Sample Result Keys: {list(results[0].keys())}")
             if "analysis" in results[0]:
                 self.logger.info("Analysis key IS present.")
             else:
                 self.logger.error("Analysis key IS MISSING!")
        
        self.logger.info(f"MATCH ANALYSIS SUMMARY for {matches[0].get('match_date') if matches else 'Unknown'}")
        self.logger.info(f"Total Fixtures Loaded: {stats_counts['total']}")
        self.logger.info(f"Qualified Over 2.5:    {stats_counts['qualified_over25']}")
        self.logger.info(f"Qualified BTTS:        {stats_counts['qualified_btts']}")
        self.logger.info(f"Has Historical Data:   {stats_counts['with_history']}")
        self.logger.info("="*50)
            
        return results

    def _calculate_team_averages(self, team: str, matches: List[Dict], last_n: int = 10) -> Dict[str, float]:
        """Calculate simple team averages from recent history."""
        team_matches = []
        for m in matches:
            if m.get("home_team") == team or m.get("away_team") == team:
                # Basic validation
                if m.get("fthg") is not None and m.get("ftag") is not None:
                     team_matches.append(m)
        
        # Sort by date
        team_matches.sort(key=lambda x: str(x.get("match_date", "1900-01-01")), reverse=True)
        recent = team_matches[:last_n]
        
        if not recent:
            return {"scored_avg": 0.0, "conceded_avg": 0.0, "win_rate": 0.0}
            
        scored = 0
        conceded = 0
        wins = 0
        
        for m in recent:
            is_home = m.get("home_team") == team
            goals_for = m.get("fthg", 0) if is_home else m.get("ftag", 0)
            goals_against = m.get("ftag", 0) if is_home else m.get("fthg", 0)
            
            scored += goals_for
            conceded += goals_against
            
            if goals_for > goals_against:
                wins += 1
                
        n = len(recent)
        return {
            "scored_avg": round(scored / n, 2),
            "conceded_avg": round(conceded / n, 2),
            "win_rate": round(wins / n, 2)
        }
