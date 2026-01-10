"""
Analysis Backtest Service.

This service performs a pure statistical backtest using the MatchAnalyzer.
It selects the single best prediction for every match based on the highest
probability across all markets (1X2, Over 2.5, BTTS), with NO confidence filtering.
"""
from dataclasses import dataclass
from typing import List, Dict, Any, Optional
from datetime import date

from src.domain.entities.match import Match
from src.application.use_cases.analyze_matches import MatchAnalyzer
from src.utils.logger import get_logger

logger = get_logger("AnalysisBacktestService")

@dataclass
class BacktestResult:
    total_matches: int
    correct_predictions: int
    accuracy: float
    market_stats: Dict[str, Dict[str, float]]
    predictions: List[Dict[str, Any]]

class AnalysisBacktestService:
    def __init__(self, match_analyzer: MatchAnalyzer):
        self.match_analyzer = match_analyzer
    
    def run_backtest(
        self,
        target_matches: List[Match],
        historical_matches: List[Dict[str, Any]], # Raw dicts for performance
    ) -> BacktestResult:
        """
        Run backtest on target matches using historical context.
        Strategy: Use highest probability market per match (Unfiltered).
        """
        logger.info(f"Starting analysis backtest on {len(target_matches)} matches")
        
        predictions = []
        market_stats = {
            "home_win": {"total": 0, "correct": 0},
            "away_win": {"total": 0, "correct": 0},
            "draw": {"total": 0, "correct": 0},
            "over_25": {"total": 0, "correct": 0},
            "btts": {"total": 0, "correct": 0},
        }
        
        for i, match in enumerate(target_matches):
            if i % 50 == 0:
                logger.info(f"Processing match {i+1}/{len(target_matches)}")
                
            try:
                # Analyze using statistical engine (No AI)
                analysis = self.match_analyzer.analyze(
                    match=match,
                    historical_matches=historical_matches
                )
                
                # Find best prediction (Highest Probability)
                best_market_key = None
                best_prob = -1.0
                
                # Check all available markets
                markets_data = analysis.aggregated_markets
                
                # Map API keys to internal logic
                # expected keys: over_25, btts, home_win, away_win, draw
                candidates = ["over_25", "btts", "home_win", "away_win", "draw"]
                
                if i == 0:
                    logger.info(f"DEBUG: markets_data type: {type(markets_data)}")
                    if isinstance(markets_data, dict):
                        logger.info(f"DEBUG: markets_data keys: {list(markets_data.keys())}")
                        if markets_data:
                            k = list(markets_data.keys())[0]
                            logger.info(f"DEBUG: First market val ({k}): {markets_data[k]}")
                
                for m_key in candidates:
                    m_data = markets_data.get(m_key)
                    if not m_data:
                        continue
                        
                    # m_data is a dict with 'probability' key
                    prob = m_data.get("probability", 0.0)
                    
                    if prob > best_prob:
                        best_prob = prob
                        best_market_key = m_key
                
                if best_market_key:
                    # Evaluate correctness
                    is_correct = self._evaluate_prediction(match, best_market_key)
                    
                    # Record stats
                    market_stats[best_market_key]["total"] += 1
                    if is_correct:
                        market_stats[best_market_key]["correct"] += 1
                        
                    predictions.append({
                        "date": str(match.match_date),
                        "match": f"{match.home_team} vs {match.away_team}",
                        "market": best_market_key,
                        "probability": round(best_prob, 3),
                        "is_correct": is_correct,
                        "actual_score": f"{match.fthg}-{match.ftag}"
                    })
                    
            except Exception as e:
                logger.error(f"Error analyzing {match.home_team} vs {match.away_team}: {e}")
                continue
                
        # Calculate summary stats
        total = len(predictions)
        correct = sum(1 for p in predictions if p["is_correct"])
        accuracy = (correct / total * 100) if total > 0 else 0.0
        
        # Calculate market accuracies
        final_market_stats = {}
        for market, stats in market_stats.items():
            m_total = stats["total"]
            m_correct = stats["correct"]
            m_acc = (m_correct / m_total * 100) if m_total > 0 else 0.0
            final_market_stats[market] = {
                "total": m_total,
                "correct": m_correct,
                "accuracy": round(m_acc, 2)
            }
            
        return BacktestResult(
            total_matches=len(target_matches),
            correct_predictions=correct,
            accuracy=round(accuracy, 2),
            market_stats=final_market_stats,
            predictions=predictions
        )

    def _evaluate_prediction(self, match: Match, market: str) -> bool:
        """Check if the predicted market won."""
        if match.fthg is None or match.ftag is None:
            return False
            
        h = match.fthg
        a = match.ftag
        
        if market == "over_25":
            return (h + a) > 2.5
        elif market == "btts":
            return (h > 0) and (a > 0)
        elif market == "home_win":
            return h > a
        elif market == "away_win":
            return a > h
        elif market == "draw":
            return h == a
            
        return False
