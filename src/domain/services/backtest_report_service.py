"""
Backtest report service - generates detailed backtest reports with qualification stats.
"""
from datetime import datetime, timedelta
from typing import Dict, List, Any, Optional
from collections import defaultdict

from src.domain.services.base_service import BaseService
from src.domain.services.match_stats_service import MatchStatsService
from src.domain.services.prediction_service import PredictionService
from src.data.cache.cache_manager import CacheManager
from src.utils.logger import get_logger


class BacktestReportService(BaseService):
    """Service for generating detailed backtest reports."""
    
    def __init__(self, cache_manager: Optional[CacheManager] = None):
        super().__init__(cache_manager)
        self.logger = get_logger("BacktestReportService")
        self.match_stats = MatchStatsService(cache_manager)
        self.prediction_service = PredictionService()
        self.prediction_service.load_models()
    
    def generate_report(
        self,
        matches: List[Dict],
        weeks: int = 15,
    ) -> Dict[str, Any]:
        """
        Generate detailed backtest report with qualification stats.
        
        Args:
            matches: All historical matches
            weeks: Number of weeks to backtest
            
        Returns:
            Detailed report with qualification and accuracy stats
        """
        # Split train/test
        latest_date = max(m.get("match_date", "")[:10] for m in matches if m.get("match_date"))
        latest = datetime.strptime(latest_date, "%Y-%m-%d")
        cutoff = latest - timedelta(weeks=weeks)
        cutoff_str = cutoff.strftime("%Y-%m-%d")
        
        train_matches = [m for m in matches if m.get("match_date", "")[:10] < cutoff_str]
        test_matches = [m for m in matches if m.get("match_date", "")[:10] >= cutoff_str]
        
        self.logger.info(f"Backtest: {len(train_matches)} train, {len(test_matches)} test")
        
        # Initialize counters
        total = 0
        qualified_total = 0
        not_qualified_total = 0
        
        # Market accuracy
        market_stats = {
            "over25": {"total": 0, "correct": 0, "qual_total": 0, "qual_correct": 0},
            "btts": {"total": 0, "correct": 0, "qual_total": 0, "qual_correct": 0},
            "result": {"total": 0, "correct": 0, "qual_total": 0, "qual_correct": 0},
        }
        
        # League stats
        league_stats = defaultdict(lambda: {
            "total": 0, "qualified": 0, "not_qualified": 0,
            "over25_correct": 0, "over25_total": 0,
            "btts_correct": 0, "btts_total": 0,
            "result_correct": 0, "result_total": 0,
        })
        
        for match in test_matches:
            home = match.get("home_team", "")
            away = match.get("away_team", "")
            league = match.get("league", "")
            
            if not home or not away:
                continue
            
            # Get actual results
            fthg = match.get("fthg") or match.get("FTHG")
            ftag = match.get("ftag") or match.get("FTAG")
            ftr = match.get("ftr") or match.get("FTR")
            
            if fthg is None or ftag is None:
                continue
            
            fthg, ftag = int(fthg), int(ftag)
            actual_o25 = (fthg + ftag) > 2.5
            actual_btts = fthg > 0 and ftag > 0
            actual_result = ftr if ftr else ("H" if fthg > ftag else "A" if ftag > fthg else "D")
            
            total += 1
            league_stats[league]["total"] += 1
            
            # Get qualification flags
            stats = self.match_stats.calculate_match_stats(home, away, train_matches, league=league)
            qual = stats.get("qualification", {})
            o25_qual = qual.get("over25_qualified", False)
            btts_qual = qual.get("btts_qualified", False)
            is_qualified = o25_qual or btts_qual
            
            if is_qualified:
                qualified_total += 1
                league_stats[league]["qualified"] += 1
            else:
                not_qualified_total += 1
                league_stats[league]["not_qualified"] += 1
            
            # Get predictions
            pred = self.prediction_service.predict_match(match, train_matches)
            pred_o25 = pred.get("over25", {}).get("prediction", "NO") == "YES"
            pred_btts = pred.get("btts", {}).get("prediction", "NO") == "YES"
            pred_result = pred.get("result", {}).get("prediction", "D")
            
            # Track Over 2.5
            market_stats["over25"]["total"] += 1
            league_stats[league]["over25_total"] += 1
            if pred_o25 == actual_o25:
                market_stats["over25"]["correct"] += 1
                league_stats[league]["over25_correct"] += 1
            if o25_qual:
                market_stats["over25"]["qual_total"] += 1
                if pred_o25 == actual_o25:
                    market_stats["over25"]["qual_correct"] += 1
            
            # Track BTTS
            market_stats["btts"]["total"] += 1
            league_stats[league]["btts_total"] += 1
            if pred_btts == actual_btts:
                market_stats["btts"]["correct"] += 1
                league_stats[league]["btts_correct"] += 1
            if btts_qual:
                market_stats["btts"]["qual_total"] += 1
                if pred_btts == actual_btts:
                    market_stats["btts"]["qual_correct"] += 1
            
            # Track Result
            market_stats["result"]["total"] += 1
            league_stats[league]["result_total"] += 1
            if pred_result == actual_result:
                market_stats["result"]["correct"] += 1
                league_stats[league]["result_correct"] += 1
        
        # Build response
        def pct(a, b):
            return round(a / b * 100, 1) if b > 0 else 0.0
        
        market_accuracy = {}
        for market, stats in market_stats.items():
            market_accuracy[market] = {
                "total": stats["total"],
                "correct": stats["correct"],
                "accuracy_pct": pct(stats["correct"], stats["total"]),
                "qualified_total": stats["qual_total"],
                "qualified_correct": stats["qual_correct"],
                "qualified_accuracy_pct": pct(stats["qual_correct"], stats["qual_total"]),
            }
        
        league_results = []
        for league, ls in league_stats.items():
            league_results.append({
                "league": league,
                "total_matches": ls["total"],
                "qualified_matches": ls["qualified"],
                "not_qualified_matches": ls["not_qualified"],
                "qualified_pct": pct(ls["qualified"], ls["total"]),
                "over25_accuracy_pct": pct(ls["over25_correct"], ls["over25_total"]),
                "btts_accuracy_pct": pct(ls["btts_correct"], ls["btts_total"]),
                "result_accuracy_pct": pct(ls["result_correct"], ls["result_total"]),
            })
        
        # Sort by total matches
        league_results.sort(key=lambda x: x["total_matches"], reverse=True)
        
        return {
            "test_period": {
                "start": cutoff_str,
                "end": latest_date,
            },
            "total_matches": total,
            "qualified_matches": qualified_total,
            "qualified_pct": pct(qualified_total, total),
            "not_qualified_matches": not_qualified_total,
            "not_qualified_pct": pct(not_qualified_total, total),
            "market_accuracy": market_accuracy,
            "league_stats": league_results,
            "generated_at": datetime.now().isoformat(),
        }
