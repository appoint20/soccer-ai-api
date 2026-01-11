from typing import Dict, Any, Optional, List
from datetime import date, datetime
from src.utils.logger import get_logger
from src.domain.entities.match import Match
from src.api.schemas import MatchAnalysisResult, BacktestResult

logger = get_logger("StatisticalBacktestService")

class StatisticalBacktestService:
    """
    Service to verify statistical qualifications against actual match results.
    
    Checks:
    - BTTS Qualified vs Result
    - Over 2.5 Qualified vs Result
    - Draw Qualified vs Result
    - Classic Draw Detected vs Result
    """

    def __init__(self, historical_repository):
        self._historical_repo = historical_repository

    def calculate_match_backtest(self, analysis: Any, actual_match_override: Optional[Dict] = None) -> Optional[Dict[str, Any]]:
        """
        Calculate backtest result for a SINGLE match analysis.
        
        Args:
            analysis: SingleMatchAnalysis object (or object with .match_analysis)
            actual_match_override: Optional dict with actual results (for mocking/testing)
            
        Returns:
            Dict representing BacktestResult fields (or None)
        """
        # 1. Get Actual Result
        actual = actual_match_override
        if not actual:
            actual = self._get_actual_result(analysis.home_team, analysis.away_team, analysis.date)
            
        if not actual:
            return None
            
        fthg = actual.get("fthg")
        ftag = actual.get("ftag")
        
        if fthg is None or ftag is None:
            return None
            
        # 2. Derive Actual Outcomes
        actual_score = f"{fthg}-{ftag}"
        actual_res = "D" if fthg == ftag else ("H" if fthg > ftag else "A")
        is_btts = fthg > 0 and ftag > 0
        is_over25 = (fthg + ftag) > 2.5
        
        # 3. Derive Predictions (Qualifications)
        if not analysis.match_analysis:
            return None
            
        ma = analysis.match_analysis
        # Determine predictions from 'Qualified' flags
        # Default to False if market is missing
        pred_btts = getattr(ma.btts, "qualified", False)
        pred_over25 = getattr(ma.over_25, "qualified", False)
        pred_draw = getattr(ma.draw, "qualified", False) # or logic?
        
        # 4. Check Correctness (Schedule-based)
        # Correct if: (Qualified AND Result Happened)
        # We generally don't count "Correctly Avoided" (Not Qualified AND Result Didn't Happen) as a "Win" in this context,
        # but for specific stats we might.
        # User wants simple "prediction correct or not".
        # If Qualified -> Predict YES.
        
        btts_correct = (pred_btts and is_btts)
        over25_correct = (pred_over25 and is_over25)
        draw_correct = (pred_draw and actual_res == "D")
        
        # 5. Build Result
        return {
            "actual_score": actual_score,
            "actual_result": actual_res,
            "is_btts": is_btts,
            "is_over25": is_over25,
            "predictions": {
                "btts": {
                    "qualified": pred_btts,
                    "correct": btts_correct if pred_btts else None 
                },
                "over25": {
                    "qualified": pred_over25,
                    "correct": over25_correct if pred_over25 else None
                },
                "draw": {
                    "qualified": pred_draw,
                    "correct": draw_correct if pred_draw else None
                }
            },
            # Compatibility with AI Backtest Structure
            "predicted_market": "Statistical Analysis", 
            "was_correct": (btts_correct or over25_correct or draw_correct) # Loose "At least one correct"
        }

    def _get_actual_result(self, home_team: str, away_team: str, match_date: Any) -> Optional[Dict[str, Any]]:
        """Fetch actual result from repo."""
        # This matches the AI service logic, can be refactored to shared utility later
        all_matches = self._historical_repo.get_all() # CAUTION: Performance heavy if repeated often.
        # Ideally, UseCase should pass the historical matches for the day.
        
        # However, for live API single execution, it's okay-ish if cached.
        
        home_lower = home_team.lower().strip()
        away_lower = away_team.lower().strip()
        
        # Handle date type
        target_date = match_date
        if isinstance(target_date, str):
            try:
                target_date = datetime.fromisoformat(target_date[:10]).date()
            except:
                pass
        
        for m in all_matches:
            # Handle obj/dict
            if hasattr(m, 'home_team'):
                m_home = m.home_team.lower().strip()
                m_away = m.away_team.lower().strip()
                m_date = m.match_date
                m_fthg = m.fthg
                m_ftag = m.ftag
            else:
                m_home = str(m.get("home_team", "")).lower().strip()
                m_away = str(m.get("away_team", "")).lower().strip()
                m_date = m.get("match_date")
                m_fthg = m.get("fthg")
                m_ftag = m.get("ftag")
                
            if isinstance(m_date, str):
                try:
                    m_date = datetime.fromisoformat(m_date[:10]).date()
                except:
                    continue
            
            if m_home == home_lower and m_away == away_lower and m_date == target_date:
                return {"fthg": m_fthg, "ftag": m_ftag}
                
        return None
