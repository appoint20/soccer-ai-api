#!/usr/bin/env python
"""
Failure analysis script.

Analyzes high-confidence predictions that failed to understand:
- What Dixon-Coles predicted vs actual
- What ML model predicted vs actual
- Team statistics at the time
- Common patterns in failures
"""
import sys
from pathlib import Path
from datetime import datetime, date, timedelta
from typing import Dict, List, Any
from collections import Counter

sys.path.insert(0, str(Path(__file__).parent.parent))

from src.data.storage.json_storage import JSONStorage
from src.statistics.dixon_coles_model import DixonColesModel
from src.domain.services.prediction_service import PredictionService
from src.domain.services.feature_engineering_service import FeatureEngineeringService
from src.domain.services.derby_service import DerbyService
from src.utils.logger import get_logger

logger = get_logger("FailureAnalysis")


# Tier mapping
TIER_MAP = {
    "E0": "tier1", "D1": "tier1", "I1": "tier1", "SP1": "tier1", "F1": "tier1",
    "E1": "tier2", "I2": "tier2", "F2": "tier2",
    "E2": "tier3", "E3": "tier3",
}


def load_matches() -> List[Dict]:
    storage = JSONStorage()
    return storage.load("data/processed/matches.json") or []


def filter_before_date(matches: List[Dict], cutoff: date) -> List[Dict]:
    result = []
    for m in matches:
        d = m.get("match_date")
        if d:
            if isinstance(d, str):
                d = datetime.fromisoformat(d[:10]).date()
            if d < cutoff:
                result.append(m)
    return result


def filter_by_date_range(matches: List[Dict], start: date, end: date) -> List[Dict]:
    result = []
    for m in matches:
        d = m.get("match_date")
        if d:
            if isinstance(d, str):
                d = datetime.fromisoformat(d[:10]).date()
            if start <= d <= end:
                result.append(m)
    return result


def analyze_failures(weeks: int = 10, market: str = "result", min_confidence: float = 0.60):
    """
    Analyze failed high-confidence predictions.
    
    Args:
        weeks: Test period in weeks
        market: 'result', 'over25', or 'btts'
        min_confidence: Minimum confidence threshold
    """
    print("=" * 70)
    print(f"FAILURE ANALYSIS: {market.upper()} (>{min_confidence:.0%} confidence)")
    print("=" * 70)
    print()
    
    # Load data
    print("[1/4] Loading data...")
    all_matches = load_matches()
    derby_service = DerbyService()
    feature_service = FeatureEngineeringService()
    
    # Date range
    dates = []
    for m in all_matches:
        d = m.get("match_date")
        if d:
            if isinstance(d, str):
                d = datetime.fromisoformat(d[:10]).date()
            dates.append(d)
    
    latest = max(dates)
    test_start = latest - timedelta(weeks=weeks)
    
    # Get test matches
    test_matches = filter_by_date_range(all_matches, test_start, latest)
    historical = filter_before_date(all_matches, test_start)
    
    print(f"  Test matches: {len(test_matches)}")
    print()
    
    # Initialize models
    print("[2/4] Initializing models...")
    dc = DixonColesModel(xi=0.01, rho=-0.10)  # Optimized params
    dc.fit(historical)
    
    ml_services = {}
    for tier in ["tier1", "tier2", "tier3"]:
        ml = PredictionService(tier=tier)
        ml.load_models(tier=tier)
        ml_services[tier] = ml
    
    print()
    print("[3/4] Finding failed predictions...")
    print()
    
    failures = []
    successes = []
    
    for match in test_matches:
        home = match.get("home_team", "")
        away = match.get("away_team", "")
        fthg = match.get("fthg")
        ftag = match.get("ftag")
        ftr = match.get("ftr", "D")
        league = match.get("league", "E0")
        tier = TIER_MAP.get(league, "tier1")
        
        if fthg is None or ftag is None:
            continue
        
        # Get match date for time-travel
        match_date = match.get("match_date")
        if isinstance(match_date, str):
            match_date = datetime.fromisoformat(match_date[:10]).date()
        
        history = filter_before_date(all_matches, match_date)
        
        # Dixon-Coles predictions
        dc_over25 = dc.predict_over25_prob(home, away)
        dc_btts = dc.predict_btts_prob(home, away)
        dc_result = dc.predict_1x2(home, away)
        dc_home_xg, dc_away_xg = dc.get_expected_goals(home, away)
        
        # Determine DC prediction
        if market == "result":
            max_prob = max(dc_result.values())
            if max_prob < min_confidence:
                continue  # Skip low confidence
            
            dc_pred = max(dc_result, key=dc_result.get)
            if dc_pred == "home_win":
                dc_pred = "H"
            elif dc_pred == "away_win":
                dc_pred = "A"
            else:
                dc_pred = "D"
            
            actual = ftr
            confidence = max_prob
            
        elif market == "over25":
            confidence = abs(dc_over25 - 0.5) + 0.5
            if confidence < min_confidence:
                continue
            
            dc_pred = "YES" if dc_over25 > 0.5 else "NO"
            actual = "YES" if (fthg + ftag) > 2.5 else "NO"
            
        elif market == "btts":
            confidence = abs(dc_btts - 0.5) + 0.5
            if confidence < min_confidence:
                continue
            
            dc_pred = "YES" if dc_btts > 0.5 else "NO"
            actual = "YES" if (fthg > 0 and ftag > 0) else "NO"
        
        is_correct = dc_pred == actual
        
        # Get ML prediction for comparison
        try:
            ml_pred_full = ml_services[tier].predict_match(match, history)
            
            if market == "result":
                ml_result = ml_pred_full.get("result", {})
                ml_probs = ml_result.get("probabilities", {})
                ml_pred = ml_result.get("prediction", "D")
            elif market == "over25":
                ml_over25 = ml_pred_full.get("over25", {})
                ml_pred = ml_over25.get("prediction", "NO")
                ml_probs = {"probability": ml_over25.get("probability", 0.5)}
            elif market == "btts":
                ml_btts = ml_pred_full.get("btts", {})
                ml_pred = ml_btts.get("prediction", "NO")
                ml_probs = {"probability": ml_btts.get("probability", 0.5)}
        except Exception as e:
            ml_pred = "N/A"
            ml_probs = {}
        
        # Collect info
        info = {
            "match": f"{home} vs {away}",
            "date": str(match_date),
            "league": league,
            "score": f"{fthg}-{ftag}",
            "actual": actual,
            "dc_prediction": dc_pred,
            "dc_confidence": confidence,
            "dc_result_probs": dc_result,
            "dc_xg": f"{dc_home_xg:.2f}-{dc_away_xg:.2f}",
            "ml_prediction": ml_pred,
            "ml_probs": ml_probs,
            "is_derby": derby_service.is_derby(home, away),
            "correct": is_correct,
        }
        
        if is_correct:
            successes.append(info)
        else:
            failures.append(info)
    
    # Analyze failures
    print(f"High-confidence predictions: {len(failures) + len(successes)}")
    print(f"Correct: {len(successes)} ({len(successes)/(len(failures)+len(successes)):.1%})")
    print(f"Failed: {len(failures)} ({len(failures)/(len(failures)+len(successes)):.1%})")
    print()
    
    print("[4/4] Analyzing failure patterns...")
    print()
    print("=" * 70)
    print(f"TOP 15 FAILED PREDICTIONS (>{min_confidence:.0%} confidence)")
    print("=" * 70)
    print()
    
    # Sort by confidence (highest confidence failures first)
    failures.sort(key=lambda x: x["dc_confidence"], reverse=True)
    
    for i, fail in enumerate(failures[:15]):
        print(f"{'─' * 70}")
        print(f"FAILURE #{i+1}: {fail['match']}")
        print(f"{'─' * 70}")
        print(f"  Date: {fail['date']} | League: {fail['league']}")
        print(f"  Score: {fail['score']} | Actual: {fail['actual']}")
        print(f"  Is Derby: {'YES 🔥' if fail['is_derby'] else 'No'}")
        print()
        print(f"  DIXON-COLES:")
        print(f"    Prediction: {fail['dc_prediction']} (confidence: {fail['dc_confidence']:.1%})")
        print(f"    Expected Goals: {fail['dc_xg']}")
        print(f"    1X2 Probs: H={fail['dc_result_probs'].get('home_win', 0):.1%}, D={fail['dc_result_probs'].get('draw', 0):.1%}, A={fail['dc_result_probs'].get('away_win', 0):.1%}")
        print()
        print(f"  ML MODEL:")
        print(f"    Prediction: {fail['ml_prediction']}")
        if fail['ml_probs']:
            if 'home_win' in fail['ml_probs']:
                print(f"    Probs: H={fail['ml_probs'].get('home_win', 0):.1%}, D={fail['ml_probs'].get('draw', 0):.1%}, A={fail['ml_probs'].get('away_win', 0):.1%}")
            elif 'probability' in fail['ml_probs']:
                print(f"    Probability: {fail['ml_probs'].get('probability', 0):.1%}")
        print()
    
    # Pattern analysis
    print("=" * 70)
    print("FAILURE PATTERNS")
    print("=" * 70)
    print()
    
    # League distribution
    league_counter = Counter(f["league"] for f in failures)
    print("By League:")
    for league, count in league_counter.most_common(5):
        total_in_league = sum(1 for m in (failures + successes) if m["league"] == league)
        fail_rate = count / total_in_league if total_in_league else 0
        print(f"  {league}: {count} failures ({fail_rate:.1%} fail rate)")
    print()
    
    # Derby failures
    derby_failures = sum(1 for f in failures if f["is_derby"])
    print(f"Derby matches in failures: {derby_failures}/{len(failures)} ({derby_failures/len(failures):.1%})")
    print()
    
    # Prediction type distribution
    pred_counter = Counter(f["dc_prediction"] for f in failures)
    print("By Predicted Outcome:")
    for pred, count in pred_counter.most_common():
        print(f"  Predicted {pred}: {count} failures")
    print()
    
    # Actual outcome distribution  
    actual_counter = Counter(f["actual"] for f in failures)
    print("Actual Outcomes of Failed Predictions:")
    for actual, count in actual_counter.most_common():
        print(f"  Actual {actual}: {count}")
    print()
    
    # ML agreement
    ml_agreed = sum(1 for f in failures if f["dc_prediction"] == f["ml_prediction"])
    print(f"ML agreed with Dixon-Coles on failures: {ml_agreed}/{len(failures)} ({ml_agreed/len(failures):.1%})")
    print()


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument("--weeks", type=int, default=10)
    parser.add_argument("--market", type=str, default="result", choices=["result", "over25", "btts"])
    parser.add_argument("--confidence", type=float, default=0.60)
    args = parser.parse_args()
    
    analyze_failures(args.weeks, args.market, args.confidence)
