"""
Backtest service for evaluating model performance.

Runs backtesting on historical data with:
- Per league accuracy
- Per market accuracy
- Qualified/ignored matches
- Model agreement accuracy
"""
from typing import Dict, List, Any, Optional
from datetime import date, datetime, timedelta
from collections import defaultdict

from src.domain.services.base_service import BaseService
from src.domain.services.prediction_service import PredictionService
from src.statistics.dixon_coles_model import DixonColesModel
from src.statistics.monte_carlo import MonteCarloPredictor
from src.domain.services.derby_service import DerbyService
from src.data.cache.cache_manager import CacheManager


class BacktestService(BaseService):
    """
    Backtest service for model performance evaluation.
    
    Runs time-travel backtesting with multiple models and tracks agreement.
    """
    
    TIER_MAP = {
        "E0": "tier1", "D1": "tier1", "I1": "tier1", "SP1": "tier1", "F1": "tier1",
        "E1": "tier2", "I2": "tier2", "F2": "tier2",
        "E2": "tier3", "E3": "tier3",
    }
    
    LEAGUE_NAMES = {
        "E0": "Premier League",
        "E1": "Championship",
        "E2": "League One",
        "E3": "League Two",
        "D1": "Bundesliga",
        "I1": "Serie A",
        "I2": "Serie B",
        "SP1": "La Liga",
        "F1": "Ligue 1",
        "F2": "Ligue 2",
    }
    
    def __init__(
        self,
        cache_manager: Optional[CacheManager] = None,
        confidence_threshold: float = 0.55,
    ):
        super().__init__(cache_manager)
        
        self.confidence_threshold = confidence_threshold
        self.derby_service = DerbyService()
        
        # Models
        self.dc_model: Optional[DixonColesModel] = None
        self.mc_model: Optional[MonteCarloPredictor] = None
        self.ml_services: Dict[str, PredictionService] = {}
    
    def run_backtest(
        self,
        all_matches: List[Dict],
        weeks: int = 10,
        exclude_derbies: bool = False,
    ) -> Dict[str, Any]:
        """Run backtesting with model agreement tracking."""
        self.logger.info(f"Running backtest for {weeks} weeks")
        
        # Get date range
        dates = []
        for m in all_matches:
            d = m.get("match_date")
            if d:
                if isinstance(d, str):
                    d = datetime.fromisoformat(d[:10]).date()
                elif isinstance(d, datetime):
                    d = d.date()
                dates.append(d)
        
        if not dates:
            return {"error": "No matches found"}
        
        latest = max(dates)
        test_start = latest - timedelta(weeks=weeks)
        
        # Filter matches
        train_matches = [m for m in all_matches if self._get_date(m) and self._get_date(m) < test_start]
        test_matches = [m for m in all_matches if self._get_date(m) and test_start <= self._get_date(m) <= latest]
        
        self.logger.info(f"Train: {len(train_matches)}, Test: {len(test_matches)}")
        
        # Initialize all models
        self.dc_model = DixonColesModel(xi=0.01, rho=-0.10)
        self.dc_model.fit(train_matches)
        
        self.mc_model = MonteCarloPredictor(n_simulations=1000)  # Less for speed
        self.mc_model.fit(train_matches)
        
        # Load ML models
        for tier in ["tier1", "tier2", "tier3"]:
            try:
                ml = PredictionService(tier=tier)
                ml.load_models(tier=tier)
                self.ml_services[tier] = ml
            except Exception as e:
                self.logger.warning(f"Could not load {tier} ML models: {e}")
        
        # Track results
        league_results = defaultdict(lambda: {
            "total": 0, "qualified": 0, "risky": 0,
            "over25": {"correct": 0, "total": 0},
            "btts": {"correct": 0, "total": 0},
            "result": {"correct": 0, "total": 0},
        })
        
        market_results = {
            "over25": {"correct": 0, "total": 0, "qualified": 0, "risky": 0},
            "btts": {"correct": 0, "total": 0, "qualified": 0, "risky": 0},
            "result": {"correct": 0, "total": 0, "qualified": 0, "risky": 0},
        }
        
        # Model agreement tracking
        agreement_results = {
            "over25": {
                "all_agree": {"correct": 0, "total": 0},
                "2_agree": {"correct": 0, "total": 0},
                "disagree": {"correct": 0, "total": 0},
            },
            "btts": {
                "all_agree": {"correct": 0, "total": 0},
                "2_agree": {"correct": 0, "total": 0},
                "disagree": {"correct": 0, "total": 0},
            },
            "result": {
                "all_agree": {"correct": 0, "total": 0},
                "2_agree": {"correct": 0, "total": 0},
                "disagree": {"correct": 0, "total": 0},
            },
        }
        
        weekly_results = []
        derbies_excluded = 0
        
        for week in range(weeks):
            week_start = test_start + timedelta(weeks=week)
            week_end = week_start + timedelta(days=6)
            
            week_matches = [
                m for m in test_matches
                if self._get_date(m) and week_start <= self._get_date(m) <= week_end
            ]
            
            week_stats = {
                "week": week + 1,
                "start_date": str(week_start),
                "end_date": str(week_end),
                "total_matches": len(week_matches),
                "all_agree_correct": 0,
                "all_agree_total": 0,
            }
            
            for match in week_matches:
                home = match.get("home_team", "")
                away = match.get("away_team", "")
                fthg = match.get("fthg")
                ftag = match.get("ftag")
                ftr = match.get("ftr", "D")
                league = match.get("league", "E0")
                tier = self.TIER_MAP.get(league, "tier1")
                
                if fthg is None or ftag is None:
                    continue
                
                league_results[league]["total"] += 1
                
                # Check derby
                is_derby = self.derby_service.is_derby(home, away)
                if exclude_derbies and is_derby:
                    derbies_excluded += 1
                    league_results[league]["risky"] += 1
                    continue
                
                # Get predictions from all models
                # Dixon-Coles
                dc_o25 = self.dc_model.predict_over25_prob(home, away) > 0.5
                dc_btts = self.dc_model.predict_btts_prob(home, away) > 0.5
                dc_result_probs = self.dc_model.predict_1x2(home, away)
                dc_result = self._get_result_pred(dc_result_probs)
                dc_max_prob = max(dc_result_probs.values())
                
                # Monte Carlo
                mc_pred = self.mc_model.predict(home, away)
                mc_o25 = mc_pred.get("over25", 0.5) > 0.5
                mc_btts = mc_pred.get("btts", 0.5) > 0.5
                mc_result = mc_pred.get("prediction", "D")
                
                # ML Model
                ml_o25, ml_btts, ml_result = self._get_ml_preds(match, train_matches, tier)
                
                # Actual outcomes
                actual_over25 = (fthg + ftag) > 2.5
                actual_btts = fthg > 0 and ftag > 0
                
                # Count agreement for Over 2.5
                o25_preds = [dc_o25, mc_o25, ml_o25]
                o25_count = sum(1 for p in o25_preds if p)
                o25_majority = o25_count >= 2
                
                if o25_count == 3 or o25_count == 0:  # All agree
                    agreement_results["over25"]["all_agree"]["total"] += 1
                    if (o25_count == 3) == actual_over25:
                        agreement_results["over25"]["all_agree"]["correct"] += 1
                elif o25_count == 2 or o25_count == 1:  # 2 agree
                    agreement_results["over25"]["2_agree"]["total"] += 1
                    if o25_majority == actual_over25:
                        agreement_results["over25"]["2_agree"]["correct"] += 1
                
                market_results["over25"]["total"] += 1
                if o25_majority == actual_over25:
                    market_results["over25"]["correct"] += 1
                
                league_results[league]["over25"]["total"] += 1
                if o25_majority == actual_over25:
                    league_results[league]["over25"]["correct"] += 1
                
                # BTTS agreement
                btts_preds = [dc_btts, mc_btts, ml_btts]
                btts_count = sum(1 for p in btts_preds if p)
                btts_majority = btts_count >= 2
                
                if btts_count == 3 or btts_count == 0:
                    agreement_results["btts"]["all_agree"]["total"] += 1
                    if (btts_count == 3) == actual_btts:
                        agreement_results["btts"]["all_agree"]["correct"] += 1
                elif btts_count == 2 or btts_count == 1:
                    agreement_results["btts"]["2_agree"]["total"] += 1
                    if btts_majority == actual_btts:
                        agreement_results["btts"]["2_agree"]["correct"] += 1
                
                market_results["btts"]["total"] += 1
                if btts_majority == actual_btts:
                    market_results["btts"]["correct"] += 1
                
                league_results[league]["btts"]["total"] += 1
                if btts_majority == actual_btts:
                    league_results[league]["btts"]["correct"] += 1
                
                # Result agreement
                result_preds = [dc_result, mc_result, ml_result]
                result_counts = {}
                for p in result_preds:
                    result_counts[p] = result_counts.get(p, 0) + 1
                
                majority_result = max(result_counts, key=result_counts.get)
                max_count = result_counts[majority_result]
                
                if max_count == 3:  # All agree
                    agreement_results["result"]["all_agree"]["total"] += 1
                    week_stats["all_agree_total"] += 1
                    if majority_result == ftr:
                        agreement_results["result"]["all_agree"]["correct"] += 1
                        week_stats["all_agree_correct"] += 1
                elif max_count == 2:
                    agreement_results["result"]["2_agree"]["total"] += 1
                    if majority_result == ftr:
                        agreement_results["result"]["2_agree"]["correct"] += 1
                else:
                    agreement_results["result"]["disagree"]["total"] += 1
                    if majority_result == ftr:
                        agreement_results["result"]["disagree"]["correct"] += 1
                
                # Confidence check (mark as risky if low)
                is_confident = dc_max_prob >= self.confidence_threshold
                
                market_results["result"]["total"] += 1
                if is_confident:
                    market_results["result"]["qualified"] += 1
                    league_results[league]["qualified"] += 1
                else:
                    market_results["result"]["risky"] += 1
                    league_results[league]["risky"] += 1
                
                if majority_result == ftr:
                    market_results["result"]["correct"] += 1
                
                league_results[league]["result"]["total"] += 1
                if majority_result == ftr:
                    league_results[league]["result"]["correct"] += 1
            
            weekly_results.append(week_stats)
        
        # Calculate league accuracy
        league_summary = []
        for league, data in league_results.items():
            league_info = {
                "league_code": league,
                "league_name": self.LEAGUE_NAMES.get(league, league),
                "total_matches": data["total"],
                "qualified_matches": data["qualified"],
                "risky_matches": data["risky"],
                "accuracy": {
                    "over25": round(data["over25"]["correct"] / data["over25"]["total"], 4)
                        if data["over25"]["total"] > 0 else 0,
                    "btts": round(data["btts"]["correct"] / data["btts"]["total"], 4)
                        if data["btts"]["total"] > 0 else 0,
                    "result": round(data["result"]["correct"] / data["result"]["total"], 4)
                        if data["result"]["total"] > 0 else 0,
                },
            }
            league_summary.append(league_info)
        
        league_summary.sort(key=lambda x: x["total_matches"], reverse=True)
        
        # Market summary
        market_summary = {}
        for market, data in market_results.items():
            market_summary[market] = {
                "total_predictions": data["total"],
                "qualified_predictions": data.get("qualified", data["total"]),
                "correct_predictions": data["correct"],
                "accuracy": round(data["correct"] / data["total"], 4) if data["total"] > 0 else 0,
            }
        
        # Agreement summary
        agreement_summary = {}
        for market, levels in agreement_results.items():
            agreement_summary[market] = {}
            for level, counts in levels.items():
                agreement_summary[market][level] = {
                    "total": counts["total"],
                    "correct": counts["correct"],
                    "accuracy": round(counts["correct"] / counts["total"], 4) if counts["total"] > 0 else 0,
                }
        
        return {
            "summary": {
                "test_period": {
                    "start": str(test_start),
                    "end": str(latest),
                    "weeks": weeks,
                },
                "total_matches": len(test_matches),
                "qualified_matches": market_results["result"]["qualified"],
                "risky_matches": market_results["result"]["risky"],
                "derbies_excluded": derbies_excluded,
                "confidence_threshold": self.confidence_threshold,
            },
            "market_accuracy": market_summary,
            "model_agreement": agreement_summary,
            "league_accuracy": league_summary,
            "weekly_breakdown": weekly_results,
        }
    
    def _get_result_pred(self, probs: Dict) -> str:
        """Get result prediction from probabilities."""
        max_key = max(probs, key=probs.get)
        if max_key == "home_win":
            return "H"
        elif max_key == "away_win":
            return "A"
        return "D"
    
    def _get_ml_preds(self, match: Dict, history: List[Dict], tier: str):
        """Get ML model predictions."""
        ml_service = self.ml_services.get(tier)
        if not ml_service:
            return True, True, "D"  # Default if no model
        
        try:
            pred = ml_service.predict_match(match, history)
            o25 = pred.get("over25", {}).get("prediction", "NO") == "YES"
            btts = pred.get("btts", {}).get("prediction", "NO") == "YES"
            result = pred.get("result", {}).get("prediction", "D")
            return o25, btts, result
        except Exception:
            return True, True, "D"
    
    def _get_date(self, match: Dict) -> Optional[date]:
        """Extract date from match."""
        d = match.get("match_date")
        if d:
            if isinstance(d, str):
                return datetime.fromisoformat(d[:10]).date()
            elif isinstance(d, datetime):
                return d.date()
            elif isinstance(d, date):
                return d
        return None
