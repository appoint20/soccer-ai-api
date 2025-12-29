"""
Comprehensive match analysis service.

Combines all prediction models:
- ML models (XGBoost/LightGBM)
- Monte Carlo simulation
- Dixon-Coles Poisson
- Team/H2H/Match statistics

Provides ensemble predictions with confidence from all models.
"""
from typing import Dict, List, Any, Optional
from datetime import date, datetime

from src.domain.services.base_service import BaseService
from src.domain.services.prediction_service import PredictionService
from src.domain.services.feature_engineering_service import FeatureEngineeringService
from src.domain.services.team_stats_service import TeamStatsService
from src.domain.services.h2h_service import H2HService
from src.domain.services.derby_service import DerbyService
from src.statistics.dixon_coles_model import DixonColesModel
from src.statistics.monte_carlo import MonteCarloPredictor
from src.data.cache.cache_manager import CacheManager


class ComprehensiveAnalysisService(BaseService):
    """
    Comprehensive match analysis combining all prediction methods.
    
    Provides:
    - Team statistics (form, goals, etc.)
    - H2H statistics
    - ML predictions with reasons
    - Monte Carlo simulations
    - Dixon-Coles predictions
    - Ensemble combined predictions
    """
    
    # Tier mapping for leagues
    TIER_MAP = {
        "E0": "tier1", "D1": "tier1", "I1": "tier1", "SP1": "tier1", "F1": "tier1",
        "E1": "tier2", "I2": "tier2", "F2": "tier2",
        "E2": "tier3", "E3": "tier3",
    }
    
    def __init__(
        self,
        cache_manager: Optional[CacheManager] = None,
        mc_simulations: int = 5000,
    ):
        """
        Initialize comprehensive analysis service.
        
        Args:
            cache_manager: Optional cache manager
            mc_simulations: Number of Monte Carlo simulations
        """
        super().__init__(cache_manager)
        
        # Services
        self.feature_service = FeatureEngineeringService(cache_manager=cache_manager)
        self.team_stats = TeamStatsService(cache_manager)
        self.h2h_service = H2HService(cache_manager)
        self.derby_service = DerbyService()
        
        # ML Services per tier
        self.ml_services: Dict[str, PredictionService] = {}
        
        # Statistical models
        self.dixon_coles = DixonColesModel(xi=0.01, rho=-0.10)  # Optimized params
        self.monte_carlo = MonteCarloPredictor(n_simulations=mc_simulations)
        
        self.is_initialized = False
    
    def initialize(self, historical_matches: List[Dict]) -> None:
        """
        Initialize all models with historical data.
        
        Args:
            historical_matches: List of historical match dicts
        """
        self.logger.info("Initializing comprehensive analysis service...")
        
        # Initialize ML services
        for tier in ["tier1", "tier2", "tier3"]:
            try:
                ml = PredictionService(tier=tier)
                ml.load_models(tier=tier)
                self.ml_services[tier] = ml
            except Exception as e:
                self.logger.warning(f"Failed to load {tier} models: {e}")
        
        # Fit statistical models
        self.dixon_coles.fit(historical_matches)
        self.monte_carlo.fit(historical_matches)
        
        self.is_initialized = True
        self.logger.info("Comprehensive analysis service initialized")
    
    def analyze_match(
        self,
        match: Dict[str, Any],
        historical_matches: List[Dict],
    ) -> Dict[str, Any]:
        """
        Perform comprehensive analysis on a single match.
        
        Args:
            match: Match dict with home_team, away_team, league, etc.
            historical_matches: All historical matches for context
            
        Returns:
            Comprehensive analysis with all predictions
        """
        home_team = match.get("home_team", "")
        away_team = match.get("away_team", "")
        league = match.get("league", "E0")
        tier = self.TIER_MAP.get(league, "tier1")
        match_date = match.get("match_date")
        
        # Get date for stats calculation
        as_of_date = None
        if match_date:
            if isinstance(match_date, str):
                as_of_date = datetime.fromisoformat(match_date[:10]).date()
            elif isinstance(match_date, date):
                as_of_date = match_date
        
        # 1. Team Statistics
        home_stats = self.team_stats.calculate_team_stats(
            home_team, historical_matches, as_of_date, league
        )
        away_stats = self.team_stats.calculate_team_stats(
            away_team, historical_matches, as_of_date, league
        )
        
        # 2. H2H Statistics
        h2h_stats = self.h2h_service.get_h2h_stats(
            home_team, away_team, historical_matches
        )
        
        # 3. Derby Detection
        is_derby = self.derby_service.is_derby(home_team, away_team)
        derby_info = self.derby_service.get_derby_info(home_team, away_team)
        
        # 4. ML Predictions
        ml_prediction = self._get_ml_prediction(match, historical_matches, tier)
        
        # 5. Dixon-Coles Predictions
        dc_prediction = self._get_dixon_coles_prediction(home_team, away_team)
        
        # 6. Monte Carlo Predictions
        mc_prediction = self._get_monte_carlo_prediction(home_team, away_team)
        
        # 7. Ensemble (combine all)
        ensemble = self._combine_predictions(ml_prediction, dc_prediction, mc_prediction)
        
        # 8. Generate reasons
        reasons = self._generate_reasons(
            home_stats, away_stats, h2h_stats, 
            ml_prediction, dc_prediction, mc_prediction,
            is_derby
        )
        
        return {
            "match_info": {
                "home_team": home_team,
                "away_team": away_team,
                "league": league,
                "date": str(match_date)[:10] if match_date else "",
                "time": match.get("time"),
                "is_derby": is_derby,
                "derby_name": derby_info.get("name") if derby_info else None,
            },
            "team_stats": {
                "home": self._summarize_team_stats(home_stats),
                "away": self._summarize_team_stats(away_stats),
            },
            "h2h": self._summarize_h2h(h2h_stats),
            "predictions": {
                "ml": ml_prediction,
                "dixon_coles": dc_prediction,
                "monte_carlo": mc_prediction,
                "ensemble": ensemble,
            },
            "reasons": reasons,
            "confidence_summary": self._get_confidence_summary(ensemble),
        }
    
    def _get_ml_prediction(
        self,
        match: Dict,
        historical: List[Dict],
        tier: str,
    ) -> Dict[str, Any]:
        """Get ML model predictions."""
        ml_service = self.ml_services.get(tier)
        
        if not ml_service:
            return {"error": f"No ML model for {tier}"}
        
        try:
            pred = ml_service.predict_match(match, historical)
            return {
                "over25": {
                    "prediction": pred.get("over25", {}).get("prediction", "NO"),
                    "probability": pred.get("over25", {}).get("probability", 0.5),
                    "confidence": pred.get("over25", {}).get("confidence", "LOW"),
                },
                "btts": {
                    "prediction": pred.get("btts", {}).get("prediction", "NO"),
                    "probability": pred.get("btts", {}).get("probability", 0.5),
                    "confidence": pred.get("btts", {}).get("confidence", "LOW"),
                },
                "result": {
                    "prediction": pred.get("result", {}).get("prediction", "D"),
                    "probabilities": pred.get("result", {}).get("probabilities", {}),
                    "confidence": pred.get("result", {}).get("confidence", "LOW"),
                },
            }
        except Exception as e:
            self.logger.error(f"ML prediction failed: {e}")
            return {"error": str(e)}
    
    def _get_dixon_coles_prediction(
        self,
        home_team: str,
        away_team: str,
    ) -> Dict[str, Any]:
        """Get Dixon-Coles predictions."""
        try:
            home_xg, away_xg = self.dixon_coles.get_expected_goals(home_team, away_team)
            result_probs = self.dixon_coles.predict_1x2(home_team, away_team)
            over25 = self.dixon_coles.predict_over25_prob(home_team, away_team)
            btts = self.dixon_coles.predict_btts_prob(home_team, away_team)
            
            # Determine prediction
            max_prob = max(result_probs.values())
            result_pred = max(result_probs, key=result_probs.get)
            if result_pred == "home_win":
                result_pred = "H"
            elif result_pred == "away_win":
                result_pred = "A"
            else:
                result_pred = "D"
            
            return {
                "expected_goals": {
                    "home": round(home_xg, 2),
                    "away": round(away_xg, 2),
                    "total": round(home_xg + away_xg, 2),
                },
                "over25": {
                    "prediction": "YES" if over25 > 0.5 else "NO",
                    "probability": round(over25, 4),
                },
                "btts": {
                    "prediction": "YES" if btts > 0.5 else "NO",
                    "probability": round(btts, 4),
                },
                "result": {
                    "prediction": result_pred,
                    "probabilities": {
                        "home_win": result_probs.get("home_win", 0),
                        "draw": result_probs.get("draw", 0),
                        "away_win": result_probs.get("away_win", 0),
                    },
                    "confidence": self._prob_to_confidence(max_prob),
                },
            }
        except Exception as e:
            self.logger.error(f"Dixon-Coles prediction failed: {e}")
            return {"error": str(e)}
    
    def _get_monte_carlo_prediction(
        self,
        home_team: str,
        away_team: str,
    ) -> Dict[str, Any]:
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
            self.logger.error(f"Monte Carlo prediction failed: {e}")
            return {"error": str(e)}
    
    def _combine_predictions(
        self,
        ml: Dict,
        dc: Dict,
        mc: Dict,
    ) -> Dict[str, Any]:
        """Combine predictions into ensemble."""
        # Weights: ML 30%, Dixon-Coles 40%, Monte Carlo 30%
        ML_W, DC_W, MC_W = 0.30, 0.40, 0.30
        
        # Over 2.5 ensemble
        ml_o25 = ml.get("over25", {}).get("probability", 0.5) if "error" not in ml else 0.5
        dc_o25 = dc.get("over25", {}).get("probability", 0.5) if "error" not in dc else 0.5
        mc_o25 = mc.get("over25", {}).get("probability", 0.5) if "error" not in mc else 0.5
        
        ens_o25 = ml_o25 * ML_W + dc_o25 * DC_W + mc_o25 * MC_W
        
        # BTTS ensemble
        ml_btts = ml.get("btts", {}).get("probability", 0.5) if "error" not in ml else 0.5
        dc_btts = dc.get("btts", {}).get("probability", 0.5) if "error" not in dc else 0.5
        mc_btts = mc.get("btts", {}).get("probability", 0.5) if "error" not in mc else 0.5
        
        ens_btts = ml_btts * ML_W + dc_btts * DC_W + mc_btts * MC_W
        
        # Result ensemble (1X2)
        ml_probs = ml.get("result", {}).get("probabilities", {}) if "error" not in ml else {}
        dc_probs = dc.get("result", {}).get("probabilities", {}) if "error" not in dc else {}
        mc_probs = mc.get("result", {}).get("probabilities", {}) if "error" not in mc else {}
        
        ens_home = (
            ml_probs.get("home_win", 0.4) * ML_W +
            dc_probs.get("home_win", 0.4) * DC_W +
            mc_probs.get("home_win", 0.4) * MC_W
        )
        ens_draw = (
            ml_probs.get("draw", 0.27) * ML_W +
            dc_probs.get("draw", 0.27) * DC_W +
            mc_probs.get("draw", 0.27) * MC_W
        )
        ens_away = (
            ml_probs.get("away_win", 0.33) * ML_W +
            dc_probs.get("away_win", 0.33) * DC_W +
            mc_probs.get("away_win", 0.33) * MC_W
        )
        
        # Normalize
        total = ens_home + ens_draw + ens_away
        if total > 0:
            ens_home /= total
            ens_draw /= total
            ens_away /= total
        
        max_prob = max(ens_home, ens_draw, ens_away)
        if ens_home == max_prob:
            result_pred = "H"
        elif ens_away == max_prob:
            result_pred = "A"
        else:
            result_pred = "D"
        
        # Model agreement (how many agree)
        ml_result = ml.get("result", {}).get("prediction", "D") if "error" not in ml else None
        dc_result = dc.get("result", {}).get("prediction", "D") if "error" not in dc else None
        mc_result = mc.get("result", {}).get("prediction", "D") if "error" not in mc else None
        
        agreement = sum(1 for p in [ml_result, dc_result, mc_result] if p == result_pred)
        
        return {
            "over25": {
                "prediction": "YES" if ens_o25 > 0.5 else "NO",
                "probability": round(ens_o25, 4),
                "confidence": self._prob_to_confidence_binary(ens_o25),
            },
            "btts": {
                "prediction": "YES" if ens_btts > 0.5 else "NO",
                "probability": round(ens_btts, 4),
                "confidence": self._prob_to_confidence_binary(ens_btts),
            },
            "result": {
                "prediction": result_pred,
                "probabilities": {
                    "home_win": round(ens_home, 4),
                    "draw": round(ens_draw, 4),
                    "away_win": round(ens_away, 4),
                },
                "confidence": self._prob_to_confidence(max_prob),
            },
            "model_agreement": f"{agreement}/3",
            "weights": {"ml": ML_W, "dixon_coles": DC_W, "monte_carlo": MC_W},
        }
    
    def _generate_reasons(
        self,
        home_stats: Dict,
        away_stats: Dict,
        h2h: Dict,
        ml: Dict,
        dc: Dict,
        mc: Dict,
        is_derby: bool,
    ) -> Dict[str, List[str]]:
        """Generate human-readable reasons for predictions."""
        over25_reasons = []
        btts_reasons = []
        result_reasons = []
        
        # Home team form
        home_form = home_stats.get("form_last_5", {})
        home_goals_avg = home_form.get("goals_scored_avg", 0)
        
        if home_goals_avg >= 2.0:
            over25_reasons.append(f"Home team averaging {home_goals_avg:.1f} goals/game (L5)")
        
        # Away team form
        away_form = away_stats.get("form_last_5", {})
        away_goals_avg = away_form.get("goals_scored_avg", 0)
        
        if away_goals_avg >= 1.5:
            btts_reasons.append(f"Away team scoring {away_goals_avg:.1f} goals/game (L5)")
        
        # H2H
        h2h_goals = h2h.get("goal_statistics", {})
        h2h_over25_rate = h2h_goals.get("over25_rate", 0)
        
        if h2h_over25_rate > 0.6:
            over25_reasons.append(f"H2H: {h2h_over25_rate:.0%} of meetings had 3+ goals")
        
        # Dixon-Coles xG
        if "error" not in dc:
            xg = dc.get("expected_goals", {})
            total_xg = xg.get("total", 0)
            if total_xg > 2.8:
                over25_reasons.append(f"Expected {total_xg:.1f} goals (Dixon-Coles)")
            elif total_xg < 2.2:
                over25_reasons.append(f"Only {total_xg:.1f} expected goals")
        
        # Monte Carlo draw signals
        if "error" not in mc:
            draw_likely = mc.get("draw_likelihood", {}).get("is_draw_likely", False)
            if draw_likely:
                result_reasons.append("Monte Carlo signals high draw probability")
        
        # Model agreement
        ml_o25 = ml.get("over25", {}).get("prediction") if "error" not in ml else None
        dc_o25 = dc.get("over25", {}).get("prediction") if "error" not in dc else None
        mc_o25 = mc.get("over25", {}).get("prediction") if "error" not in mc else None
        
        agree_o25 = sum(1 for p in [ml_o25, dc_o25, mc_o25] if p == "YES")
        if agree_o25 == 3:
            over25_reasons.append("All 3 models agree: Over 2.5")
        elif agree_o25 == 0:
            over25_reasons.append("All 3 models agree: Under 2.5")
        
        # Derby warning
        if is_derby:
            result_reasons.append("⚠️ Derby match - historically unpredictable")
        
        return {
            "over25": over25_reasons or ["Based on statistical analysis"],
            "btts": btts_reasons or ["Based on team scoring patterns"],
            "result": result_reasons or ["Based on ensemble prediction"],
        }
    
    def _summarize_team_stats(self, stats: Dict) -> Dict:
        """Summarize team stats for response."""
        overall = stats.get("overall", {})
        form = stats.get("form_last_5", {})
        
        return {
            "matches_played": overall.get("matches", 0),
            "goals_scored_avg": round(overall.get("goals_scored_avg", 0), 2),
            "goals_conceded_avg": round(overall.get("goals_conceded_avg", 0), 2),
            "over25_rate": round(overall.get("over25_rate", 0.5), 2),
            "btts_rate": round(overall.get("btts_rate", 0.5), 2),
            "form": {
                "last_5_goals_avg": round(form.get("goals_scored_avg", 0), 2),
                "last_5_points": form.get("points", 0),
                "trend": form.get("trend", "stable"),
            },
        }
    
    def _summarize_h2h(self, h2h: Dict) -> Dict:
        """Summarize H2H stats."""
        return {
            "total_meetings": h2h.get("total_meetings", 0),
            "home_wins": h2h.get("overall_record", {}).get("home_wins", 0),
            "draws": h2h.get("overall_record", {}).get("draws", 0),
            "away_wins": h2h.get("overall_record", {}).get("away_wins", 0),
            "avg_goals": round(h2h.get("goal_statistics", {}).get("avg_total_goals", 2.5), 2),
            "over25_rate": round(h2h.get("goal_statistics", {}).get("over25_rate", 0.5), 2),
            "btts_rate": round(h2h.get("goal_statistics", {}).get("btts_rate", 0.5), 2),
        }
    
    def _prob_to_confidence(self, prob: float) -> str:
        """Convert probability to confidence level."""
        if prob >= 0.60:
            return "HIGH"
        elif prob >= 0.45:
            return "MEDIUM"
        return "LOW"
    
    def _prob_to_confidence_binary(self, prob: float) -> str:
        """Convert binary probability to confidence."""
        distance = abs(prob - 0.5)
        if distance >= 0.20:
            return "HIGH"
        elif distance >= 0.10:
            return "MEDIUM"
        return "LOW"
    
    def _get_confidence_summary(self, ensemble: Dict) -> Dict:
        """Get summary of confidence levels."""
        return {
            "over25": ensemble.get("over25", {}).get("confidence", "LOW"),
            "btts": ensemble.get("btts", {}).get("confidence", "LOW"),
            "result": ensemble.get("result", {}).get("confidence", "LOW"),
            "overall": self._get_overall_confidence(ensemble),
        }
    
    def _get_overall_confidence(self, ensemble: Dict) -> str:
        """Calculate overall confidence."""
        conf_map = {"HIGH": 3, "MEDIUM": 2, "LOW": 1}
        
        o25_conf = conf_map.get(ensemble.get("over25", {}).get("confidence", "LOW"), 1)
        btts_conf = conf_map.get(ensemble.get("btts", {}).get("confidence", "LOW"), 1)
        res_conf = conf_map.get(ensemble.get("result", {}).get("confidence", "LOW"), 1)
        
        avg = (o25_conf + btts_conf + res_conf) / 3
        
        if avg >= 2.5:
            return "HIGH"
        elif avg >= 1.5:
            return "MEDIUM"
        return "LOW"
