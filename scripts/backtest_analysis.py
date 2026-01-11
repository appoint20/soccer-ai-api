#!/usr/bin/env python
"""
Backtest using the Analysis Service (Statistical Models + Logic).
Targeting DGS (Draw Gravity Score), Classic Draw logic, and Qualification flags.
"""
import sys
import json
from pathlib import Path
from datetime import datetime, timedelta, date
from dataclasses import dataclass
from typing import List, Optional

sys.path.insert(0, str(Path(__file__).parent.parent))

from src.data.storage.json_storage import JSONStorage
from src.utils.logger import get_logger
from src.domain.entities.match import Match
from src.api.schemas import MatchAnalysisResult

# Services
from src.domain.services.team_name_matcher import TeamNameMatcher
from src.domain.services.calculators.team_form_calculator import TeamFormCalculator
from src.domain.services.calculators.h2h_stats_calculator import H2HStatsCalculator
from src.domain.services.calculators.poisson_goal_calculator import PoissonGoalCalculator
from src.domain.services.calculators.monte_carlo_uncertainty_adjuster import MonteCarloUncertaintyAdjuster
from src.domain.services.calculators.match_confidence_calculator import MatchConfidenceCalculator
from src.domain.services.calculators.qualification_calculator import QualificationCalculator
from src.application.use_cases.analyze_matches import AnalyzeMatchesUseCase, MatchAnalyzer, AnalyzeMatchesRequest

logger = get_logger("AnalysisBacktest")

# Mocks
class MockUpcomingRepository:
    def __init__(self):
        self._matches_by_date = {} # date -> list[Match]

    def set_matches(self, matches: List[Match]):
        self._matches_by_date = {}
        for m in matches:
            if m.match_date not in self._matches_by_date:
                self._matches_by_date[m.match_date] = []
            self._matches_by_date[m.match_date].append(m)

    def get_by_date(self, date_obj) -> List[Match]:
        date_str = date_obj.isoformat() if hasattr(date_obj, 'isoformat') else str(date_obj)
        # handle str vs date
        target = None
        for k in self._matches_by_date.keys():
            if str(k)[:10] == str(date_str)[:10]:
                target = k
                break
        return self._matches_by_date.get(target, [])

class MockHistoricalRepository:
    def __init__(self, matches: List[Match]):
        self._matches = matches
        
    def get_all(self) -> List[Match]:
        return self._matches
        
    def get_matches_before(self, date_obj) -> List[Match]:
        return [m for m in self._matches if m.match_date < date_obj]

    # For MatchAnalyzer, getting historial matches is usually passed directly to analyze(),
    # but the use case calls self._historical_repo.get_all().
    # Ideally for time-travel, get_all() should return FILTERED matches.
    # So we will update this mock to behave like a time-travel repo if needed.
    # But AnalyzeMatchesUseCase.execute() calls get_all() once per request.
    # If the request is for a specific date, we expect the repo to return valid history for that date?
    # No, get_all() usually implies caching EVERYTHING.
    # But for backtest, we must filter.
    # We will instantiate the repo FRESH for each week/date or use a dynamic filter.
    pass

def load_data():
    storage = JSONStorage()
    logger.info("Loading matches...")
    matches_data = storage.load("data/processed/matches.json")
    if not matches_data:
        raise ValueError("No matches found in data/processed/matches.json")
    
    # Convert dicts to Match entities
    matches = []
    failures = 0
    for d in matches_data:
        try:
            # Parse date
            d_str = d.get("match_date")
            if isinstance(d_str, str):
                match_date = datetime.fromisoformat(d_str[:10]).date()
            else:
                match_date = d_str
            
            # Use safe instantiation (only known fields)
            m = Match(
                id=d.get("match_id") or f"{d.get('home_team')}-{d.get('away_team')}-{match_date}",
                home_team=d.get("home_team"),
                away_team=d.get("away_team"),
                match_date=match_date,
                league=d.get("league", "UNKNOWN"),
                season=d.get("season", "2024-25"),
                fthg=d.get("fthg"), # Correct field name
                ftag=d.get("ftag"), # Correct field name
                ftr=d.get("ftr"),
                b365h=d.get("b365h"),
                b365d=d.get("b365d"),
                b365a=d.get("b365a"),
                b365_over25=d.get("b365_over25") or d.get("over25_odds"),
                b365_under25=d.get("b365_under25") or d.get("under25_odds"),
                home_position=d.get("home_position"),
                away_position=d.get("away_position")
            )
            matches.append(m)
        except Exception as e:
            failures += 1
            if failures == 1:
                logger.warning(f"Failed to load first match: {e}")
            continue
            
    matches.sort(key=lambda x: x.match_date)
    
    if not matches:
        raise ValueError(f"Failed to load ANY matches. {failures} failures.")
        
    logger.info(f"Loaded {len(matches)} matches (Failed: {failures})")
    return matches

class MockBacktestService:
    def __init__(self, actual_matches: List[Match]):
        self._actual_map = {m.match_key: m for m in actual_matches}
        
    def calculate_stats(self, analyses: list) -> dict:
        total = 0
        correct = 0
        by_market = {"btts": {"correct": 0, "total": 0}, "over25": {"correct": 0, "total": 0}, "draw": {"correct": 0, "total": 0}}
        
        for analysis in analyses:
            # Find actual match
            # Analysis match_id might be UUID or key. Try to rebuild key or find by team/date.
            actual = None
            # Try to find by team and date
            try:
                # Assuming analyses are from a single day or we scan all
                # self._actual_map keys are "YYYY-MM-DD_Home_vs_Away_League"
                target_key = f"{analysis.date}_{analysis.home_team}_vs_{analysis.away_team}_{analysis.league}"
                actual = self._actual_map.get(target_key)
            except:
                pass
                
            if not actual or not actual.is_completed:
                continue
                
            total += 1
            
            # --- BACKTEST EACH MARKET ---
            # 1. BTTS
            is_btts = actual.is_btts
            # Prediction: Qualified = YES, !Qualified = ? (Maybe NO, but usually we only bet if qualified)
            # For backtest "Accuracy of Qualification", we only count Qualified matches.
            # But "General Accuracy" uses Probability Threshold.
            
            # Let's use Qualified Flag as the "System Prediction"
            pred_btts = analysis.match_analysis.btts.qualified
            btts_correct = (is_btts and pred_btts) or (not is_btts and not pred_btts) 
            # Wait, if not qualified, do we predict NO? No, we just don't bet.
            # But for "Was prediction correct", we usually only care if we made a prediction.
            # If qualified=False, we didn't predict "YES". Did we predict "NO"?
            # Simplification: If Qualified, prediction is YES. Checks if result is YES.
            
            btts_sched_correct = False
            if pred_btts and is_btts:
                btts_sched_correct = True
            
            # 2. Over 2.5
            is_o25 = actual.is_over_25
            pred_o25 = analysis.match_analysis.over_25.qualified
            o25_sched_correct = False
            if pred_o25 and is_o25:
                o25_sched_correct = True
            
            # 3. Draw
            actual_res = actual.result.outcome if actual.result else None
            pred_draw = analysis.match_analysis.draw.qualified 
            draw_sched_correct = False
            if pred_draw and actual_res == "D":
                draw_sched_correct = True
            
            # Populate analysis.backtest_result
            analysis.backtest_result = {
                "actual_score": f"{actual.fthg}-{actual.ftag}",
                "actual_result": actual_res,
                "is_btts": is_btts,
                "is_over25": is_o25,
                "predictions": {
                    "btts": {"qualified": pred_btts, "correct": btts_sched_correct if pred_btts else None},
                    "over25": {"qualified": pred_o25, "correct": o25_sched_correct if pred_o25 else None},
                    "draw": {"qualified": pred_draw, "correct": draw_sched_correct if pred_draw else None}
                }
            }
            
            # Aggregates (Only for Qualified)
            by_market["btts"]["total"] += 1
            if pred_btts:
                if is_btts: by_market["btts"]["correct"] += 1
            
            by_market["over25"]["total"] += 1
            if pred_o25:
                if is_o25: by_market["over25"]["correct"] += 1
            
            if pred_draw:
                by_market["draw"]["total"] += 1
                if actual_res == "D":
                    by_market["draw"]["correct"] += 1

        return {
            "total_matches": total,
            "by_market": by_market
        }

def run_backtest(weeks=10):
    all_matches = load_data()
    if not all_matches:
        print("No matches to backtest.")
        return

    last_date = all_matches[-1].match_date
    start_date = max(all_matches[0].match_date, last_date - timedelta(weeks=weeks))

    print(f"Backtesting {weeks} weeks: {start_date} to {last_date}")
    
    # Setup Calculators
    team_matcher = TeamNameMatcher()
    form_calc = TeamFormCalculator(team_matcher=team_matcher)
    h2h_calc = H2HStatsCalculator(team_matcher=team_matcher)
    poisson_calc = PoissonGoalCalculator()
    mc_adjuster = MonteCarloUncertaintyAdjuster()
    conf_calc = MatchConfidenceCalculator()
    qual_calc = QualificationCalculator()
    
    league_names = {"E0": "Premier League", "E1": "Championship", "D1": "Bundesliga", "I1": "Serie A", "SP1": "La Liga", "F1": "Ligue 1"}

    match_analyzer = MatchAnalyzer(
        form_calculator=form_calc,
        h2h_calculator=h2h_calc,
        poisson_calculator=poisson_calc,
        monte_carlo_adjuster=mc_adjuster,
        confidence_calculator=conf_calc,
        league_names=league_names
    )
    
    mock_upcoming = MockUpcomingRepository()
    mock_backtest = MockBacktestService(all_matches)
    
    # Full Results Container
    full_results = []
    
    current_date = start_date
    while current_date <= last_date:
        # 1. Filter History (Time Travel)
        history = [m for m in all_matches if m.match_date < current_date]
        
        # 2. Get Matches for Today
        todays_matches = [m for m in all_matches if m.match_date == current_date]
        
        if not todays_matches:
            current_date += timedelta(days=1)
            continue
            
        print(f"Processing {current_date}: {len(todays_matches)} matches...")
        
        # 3. Setup Mock Repos
        mock_upcoming.set_matches(todays_matches)
        mock_history = MockHistoricalRepository(history)
        
        # 4. Use Case (Inject Mock Backtest Service)
        use_case = AnalyzeMatchesUseCase(
            upcoming_repository=mock_upcoming,
            historical_repository=mock_history,
            match_analyzer=match_analyzer,
            qualification_calculator=qual_calc,
            backtest_service=mock_backtest
        )
        
        request = AnalyzeMatchesRequest(date=current_date)
        result = use_case.execute(request)
        
        # 5. Collect for JSON dump
        for analysis in result.analyses:
            try:
                # Use safe serialization
                # match_analysis is Pydantic, so model_dump works
                ma_dump = analysis.match_analysis.model_dump()
                
                analysis_dict = {
                    "match_id": analysis.match_id,
                    "home_team": analysis.home_team,
                    "away_team": analysis.away_team,
                    "date": str(analysis.date),
                    "league": analysis.league,
                    "match_analysis": ma_dump,
                    "backtest_result": analysis.backtest_result
                }
                full_results.append(analysis_dict)
            except Exception as e:
                # Fallback
                logger.warning(f"Serialization failed: {e}")
                pass
                
        current_date += timedelta(days=1)

    # Save to file
    out_file = "data/evaluation/analysis_backtest_results.json"
    with open(out_file, "w") as f:
        json.dump(full_results, f, indent=2)
    print(f"\nSaved {len(full_results)} backtested matches to {out_file}")

if __name__ == "__main__":
    run_backtest()
