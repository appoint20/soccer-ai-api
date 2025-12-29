"""
Backtest service for evaluating model performance.

Runs backtesting on historical data with:
- Per league accuracy
- Per market accuracy
- Qualified/ignored matches
- Confidence-filtered results
"""
from typing import Dict, List, Any, Optional
from datetime import date, datetime, timedelta
from collections import defaultdict

from src.domain.services.base_service import BaseService
from src.statistics.dixon_coles_model import DixonColesModel
from src.statistics.monte_carlo import MonteCarloPredictor
from src.domain.services.derby_service import DerbyService
from src.data.cache.cache_manager import CacheManager


class BacktestService(BaseService):
    """
    Backtest service for model performance evaluation.
    
    Runs time-travel backtesting using Dixon-Coles model.
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
        """
        Initialize backtest service.
        
        Args:
            cache_manager: Optional cache
            confidence_threshold: Minimum confidence to qualify
        """
        super().__init__(cache_manager)
        
        self.confidence_threshold = confidence_threshold
        self.derby_service = DerbyService()
        self.model: Optional[DixonColesModel] = None
    
    def run_backtest(
        self,
        all_matches: List[Dict],
        weeks: int = 10,
        exclude_derbies: bool = False,
    ) -> Dict[str, Any]:
        """
        Run backtesting for specified weeks.
        
        Args:
            all_matches: All historical match data
            weeks: Number of weeks to test
            exclude_derbies: Whether to exclude derby matches
            
        Returns:
            Comprehensive backtest results
        """
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
        
        # Fit model on training data
        self.model = DixonColesModel(xi=0.01, rho=-0.10)
        self.model.fit(train_matches)
        
        # Track results
        league_results = defaultdict(lambda: {
            "total": 0, "qualified": 0, "ignored": 0,
            "over25": {"correct": 0, "total": 0},
            "btts": {"correct": 0, "total": 0},
            "result": {"correct": 0, "total": 0},
        })
        
        market_results = {
            "over25": {"correct": 0, "total": 0, "qualified": 0},
            "btts": {"correct": 0, "total": 0, "qualified": 0},
            "result": {"correct": 0, "total": 0, "qualified": 0},
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
                "total_matches": 0,
                "qualified": 0,
                "over25_accuracy": 0,
                "btts_accuracy": 0,
                "result_accuracy": 0,
            }
            
            week_o25_correct = 0
            week_btts_correct = 0
            week_result_correct = 0
            week_total = 0
            
            for match in week_matches:
                home = match.get("home_team", "")
                away = match.get("away_team", "")
                fthg = match.get("fthg")
                ftag = match.get("ftag")
                ftr = match.get("ftr", "D")
                league = match.get("league", "E0")
                
                if fthg is None or ftag is None:
                    continue
                
                league_results[league]["total"] += 1
                
                # Check derby
                if exclude_derbies and self.derby_service.is_derby(home, away):
                    derbies_excluded += 1
                    league_results[league]["ignored"] += 1
                    continue
                
                # Get predictions
                result_probs = self.model.predict_1x2(home, away)
                over25_prob = self.model.predict_over25_prob(home, away)
                btts_prob = self.model.predict_btts_prob(home, away)
                
                # Actual outcomes
                actual_over25 = (fthg + ftag) > 2.5
                actual_btts = fthg > 0 and ftag > 0
                
                # Check confidence
                max_result_prob = max(result_probs.values())
                
                # Over 2.5
                o25_confidence = abs(over25_prob - 0.5)
                if o25_confidence >= (self.confidence_threshold - 0.5):
                    pred_over25 = over25_prob > 0.5
                    is_correct = pred_over25 == actual_over25
                    
                    league_results[league]["over25"]["total"] += 1
                    market_results["over25"]["total"] += 1
                    market_results["over25"]["qualified"] += 1
                    
                    if is_correct:
                        league_results[league]["over25"]["correct"] += 1
                        market_results["over25"]["correct"] += 1
                        week_o25_correct += 1
                else:
                    league_results[league]["ignored"] += 1
                
                # BTTS
                btts_confidence = abs(btts_prob - 0.5)
                if btts_confidence >= (self.confidence_threshold - 0.5):
                    pred_btts = btts_prob > 0.5
                    is_correct = pred_btts == actual_btts
                    
                    league_results[league]["btts"]["total"] += 1
                    market_results["btts"]["total"] += 1
                    market_results["btts"]["qualified"] += 1
                    
                    if is_correct:
                        league_results[league]["btts"]["correct"] += 1
                        market_results["btts"]["correct"] += 1
                        week_btts_correct += 1
                
                # Result
                if max_result_prob >= self.confidence_threshold:
                    pred_result = max(result_probs, key=result_probs.get)
                    if pred_result == "home_win":
                        pred_result = "H"
                    elif pred_result == "away_win":
                        pred_result = "A"
                    else:
                        pred_result = "D"
                    
                    is_correct = pred_result == ftr
                    
                    league_results[league]["result"]["total"] += 1
                    league_results[league]["qualified"] += 1
                    market_results["result"]["total"] += 1
                    market_results["result"]["qualified"] += 1
                    
                    if is_correct:
                        league_results[league]["result"]["correct"] += 1
                        market_results["result"]["correct"] += 1
                        week_result_correct += 1
                
                week_total += 1
            
            # Week stats
            week_stats["total_matches"] = week_total
            week_stats["qualified"] = market_results["result"]["qualified"]
            
            if week_total > 0:
                week_stats["over25_accuracy"] = round(week_o25_correct / week_total, 4) if week_total else 0
                week_stats["btts_accuracy"] = round(week_btts_correct / week_total, 4) if week_total else 0
                week_stats["result_accuracy"] = round(week_result_correct / week_total, 4) if week_total else 0
            
            weekly_results.append(week_stats)
        
        # Calculate league accuracy
        league_summary = []
        for league, data in league_results.items():
            league_info = {
                "league_code": league,
                "league_name": self.LEAGUE_NAMES.get(league, league),
                "total_matches": data["total"],
                "qualified_matches": data["qualified"],
                "ignored_matches": data["ignored"],
                "accuracy": {
                    "over25": round(
                        data["over25"]["correct"] / data["over25"]["total"], 4
                    ) if data["over25"]["total"] > 0 else 0,
                    "btts": round(
                        data["btts"]["correct"] / data["btts"]["total"], 4
                    ) if data["btts"]["total"] > 0 else 0,
                    "result": round(
                        data["result"]["correct"] / data["result"]["total"], 4
                    ) if data["result"]["total"] > 0 else 0,
                },
            }
            league_summary.append(league_info)
        
        # Sort by total matches
        league_summary.sort(key=lambda x: x["total_matches"], reverse=True)
        
        # Calculate market summary
        market_summary = {}
        for market, data in market_results.items():
            market_summary[market] = {
                "total_predictions": data["total"],
                "qualified_predictions": data["qualified"],
                "correct_predictions": data["correct"],
                "accuracy": round(data["correct"] / data["total"], 4) if data["total"] > 0 else 0,
            }
        
        # Overall summary
        total_matches = len(test_matches)
        total_qualified = sum(d["qualified"] for d in league_results.values())
        total_ignored = total_matches - total_qualified
        
        return {
            "summary": {
                "test_period": {
                    "start": str(test_start),
                    "end": str(latest),
                    "weeks": weeks,
                },
                "total_matches": total_matches,
                "qualified_matches": total_qualified,
                "ignored_matches": total_ignored,
                "derbies_excluded": derbies_excluded,
                "confidence_threshold": self.confidence_threshold,
            },
            "market_accuracy": market_summary,
            "league_accuracy": league_summary,
            "weekly_breakdown": weekly_results,
        }
    
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
