#!/usr/bin/env python
"""
Time-travel backtesting script.

For each week in the test period:
1. Uses only historical data BEFORE that week for feature calculation
2. Makes predictions for matches in that week
3. Compares predictions with actual results
4. Calculates accuracy metrics

This simulates real-world performance without data leakage.
"""
import sys
from pathlib import Path
from datetime import datetime, date, timedelta
from typing import Dict, List, Any
import json

sys.path.insert(0, str(Path(__file__).parent.parent))

from src.data.storage.json_storage import JSONStorage
from src.domain.services.feature_engineering_service import FeatureEngineeringService
from src.domain.services.prediction_service import PredictionService
from src.utils.logger import get_logger

logger = get_logger("Backtest")


def load_matches() -> List[Dict]:
    """Load all historical matches."""
    storage = JSONStorage()
    matches = storage.load("data/processed/matches.json")
    if not matches:
        raise RuntimeError("No matches found. Run train_models.py first.")
    return matches


def filter_matches_by_date_range(
    matches: List[Dict],
    start_date: date,
    end_date: date,
) -> List[Dict]:
    """Filter matches within a date range."""
    result = []
    for m in matches:
        match_date = m.get("match_date")
        if match_date:
            if isinstance(match_date, str):
                match_date = datetime.fromisoformat(match_date[:10]).date()
            elif isinstance(match_date, datetime):
                match_date = match_date.date()
            
            if start_date <= match_date <= end_date:
                result.append(m)
    return result


def filter_matches_before_date(
    matches: List[Dict],
    cutoff_date: date,
) -> List[Dict]:
    """Filter matches strictly before a date (for training)."""
    result = []
    for m in matches:
        match_date = m.get("match_date")
        if match_date:
            if isinstance(match_date, str):
                match_date = datetime.fromisoformat(match_date[:10]).date()
            elif isinstance(match_date, datetime):
                match_date = match_date.date()
            
            if match_date < cutoff_date:
                result.append(m)
    return result


def predict_match_with_time_travel(
    match: Dict,
    historical_matches: List[Dict],
    prediction_service: PredictionService,
) -> Dict:
    """
    Make prediction using only data before the match date.
    This is the time-travel approach - no data leakage.
    """
    match_date = match.get("match_date")
    if isinstance(match_date, str):
        match_date = datetime.fromisoformat(match_date[:10]).date()
    elif isinstance(match_date, datetime):
        match_date = match_date.date()
    
    # Filter historical matches to only include those BEFORE this match
    available_history = filter_matches_before_date(historical_matches, match_date)
    
    # Make prediction
    prediction = prediction_service.predict_match(match, available_history)
    
    return prediction


def evaluate_prediction(prediction: Dict, actual: Dict) -> Dict:
    """
    Compare prediction with actual result.
    
    Returns dict with evaluation for each market.
    """
    result = {
        "match": f"{actual.get('home_team')} vs {actual.get('away_team')}",
        "date": str(actual.get("match_date", ""))[:10],
        "league": actual.get("league"),
    }
    
    # Actual values
    fthg = actual.get("fthg", 0) or 0
    ftag = actual.get("ftag", 0) or 0
    ftr = actual.get("ftr", "D")
    
    actual_over25 = (fthg + ftag) > 2.5
    actual_btts = fthg > 0 and ftag > 0
    actual_result = ftr
    
    # Over 2.5 evaluation
    over25_pred = prediction.get("over25", {})
    pred_over25 = over25_pred.get("prediction", "NO") == "YES"
    result["over25"] = {
        "predicted": "YES" if pred_over25 else "NO",
        "actual": "YES" if actual_over25 else "NO",
        "correct": pred_over25 == actual_over25,
        "probability": over25_pred.get("probability", 0.5),
        "confidence": over25_pred.get("confidence", "LOW"),
    }
    
    # BTTS evaluation
    btts_pred = prediction.get("btts", {})
    pred_btts = btts_pred.get("prediction", "NO") == "YES"
    result["btts"] = {
        "predicted": "YES" if pred_btts else "NO",
        "actual": "YES" if actual_btts else "NO",
        "correct": pred_btts == actual_btts,
        "probability": btts_pred.get("probability", 0.5),
        "confidence": btts_pred.get("confidence", "LOW"),
    }
    
    # Result evaluation
    result_pred = prediction.get("result", {})
    pred_result = result_pred.get("prediction", "D")
    result["result"] = {
        "predicted": pred_result,
        "actual": actual_result,
        "correct": pred_result == actual_result,
        "probabilities": result_pred.get("probabilities", {}),
        "confidence": result_pred.get("confidence", "LOW"),
    }
    
    return result


def calculate_weekly_stats(evaluations: List[Dict]) -> Dict:
    """Calculate accuracy stats for a week."""
    if not evaluations:
        return {"total": 0}
    
    total = len(evaluations)
    
    over25_correct = sum(1 for e in evaluations if e["over25"]["correct"])
    btts_correct = sum(1 for e in evaluations if e["btts"]["correct"])
    result_correct = sum(1 for e in evaluations if e["result"]["correct"])
    
    # High confidence accuracy
    high_conf_over25 = [e for e in evaluations if e["over25"]["confidence"] == "HIGH"]
    high_conf_btts = [e for e in evaluations if e["btts"]["confidence"] == "HIGH"]
    
    return {
        "total": total,
        "over25": {
            "correct": over25_correct,
            "accuracy": over25_correct / total if total else 0,
        },
        "btts": {
            "correct": btts_correct,
            "accuracy": btts_correct / total if total else 0,
        },
        "result": {
            "correct": result_correct,
            "accuracy": result_correct / total if total else 0,
        },
        "high_confidence": {
            "over25_count": len(high_conf_over25),
            "over25_accuracy": sum(1 for e in high_conf_over25 if e["over25"]["correct"]) / len(high_conf_over25) if high_conf_over25 else 0,
            "btts_count": len(high_conf_btts),
            "btts_accuracy": sum(1 for e in high_conf_btts if e["btts"]["correct"]) / len(high_conf_btts) if high_conf_btts else 0,
        }
    }


def run_backtest(weeks: int = 10):
    """
    Run time-travel backtest for the last N weeks.
    """
    print("=" * 60)
    print("Soccer GPT API - Time-Travel Backtest")
    print("=" * 60)
    print()
    
    # Load all matches
    print("[1/3] Loading data...")
    all_matches = load_matches()
    print(f"  Loaded {len(all_matches)} total matches")
    
    # Find date range
    dates = []
    for m in all_matches:
        match_date = m.get("match_date")
        if match_date:
            if isinstance(match_date, str):
                match_date = datetime.fromisoformat(match_date[:10]).date()
            elif isinstance(match_date, datetime):
                match_date = match_date.date()
            dates.append(match_date)
    
    if not dates:
        print("  ERROR: No valid dates in matches")
        return
    
    latest_date = max(dates)
    earliest_date = min(dates)
    print(f"  Date range: {earliest_date} to {latest_date}")
    
    # Calculate weeks to test
    end_date = latest_date
    start_date = end_date - timedelta(weeks=weeks)
    print(f"  Testing period: {start_date} to {end_date} ({weeks} weeks)")
    print()
    
    # Initialize prediction service
    print("[2/3] Initializing prediction service...")
    prediction_service = PredictionService()
    prediction_service.load_models()
    print()
    
    # Run backtest week by week
    print("[3/3] Running backtest...")
    
    all_evaluations = []
    weekly_results = []
    
    for week in range(weeks):
        week_start = start_date + timedelta(weeks=week)
        week_end = week_start + timedelta(days=6)
        
        # Get matches for this week
        week_matches = filter_matches_by_date_range(all_matches, week_start, week_end)
        
        if not week_matches:
            print(f"  Week {week + 1} ({week_start} - {week_end}): No matches")
            continue
        
        week_evaluations = []
        
        for match in week_matches:
            # Skip if no actual result
            if match.get("fthg") is None or match.get("ftag") is None:
                continue
            
            try:
                # Predict with time-travel (only past data)
                prediction = predict_match_with_time_travel(
                    match, all_matches, prediction_service
                )
                
                # Evaluate prediction
                evaluation = evaluate_prediction(prediction, match)
                week_evaluations.append(evaluation)
                all_evaluations.append(evaluation)
                
            except Exception as e:
                logger.error(f"Failed to process match: {e}")
                continue
        
        # Calculate week stats
        week_stats = calculate_weekly_stats(week_evaluations)
        week_stats["week"] = week + 1
        week_stats["start_date"] = str(week_start)
        week_stats["end_date"] = str(week_end)
        weekly_results.append(week_stats)
        
        # Print week summary
        if week_stats["total"] > 0:
            print(f"  Week {week + 1} ({week_start}): {week_stats['total']} matches | "
                  f"O2.5: {week_stats['over25']['accuracy']:.1%} | "
                  f"BTTS: {week_stats['btts']['accuracy']:.1%} | "
                  f"Result: {week_stats['result']['accuracy']:.1%}")
    
    # Overall summary
    print()
    print("=" * 60)
    print("BACKTEST RESULTS SUMMARY")
    print("=" * 60)
    
    overall_stats = calculate_weekly_stats(all_evaluations)
    
    print(f"\nTotal matches tested: {overall_stats['total']}")
    print()
    print("Overall Accuracy:")
    print(f"  Over 2.5: {overall_stats['over25']['accuracy']:.1%} ({overall_stats['over25']['correct']}/{overall_stats['total']})")
    print(f"  BTTS:     {overall_stats['btts']['accuracy']:.1%} ({overall_stats['btts']['correct']}/{overall_stats['total']})")
    print(f"  Result:   {overall_stats['result']['accuracy']:.1%} ({overall_stats['result']['correct']}/{overall_stats['total']})")
    print()
    print("High Confidence Predictions:")
    hc = overall_stats.get("high_confidence", {})
    print(f"  Over 2.5: {hc.get('over25_accuracy', 0):.1%} ({hc.get('over25_count', 0)} predictions)")
    print(f"  BTTS:     {hc.get('btts_accuracy', 0):.1%} ({hc.get('btts_count', 0)} predictions)")
    print()
    
    # Save results
    results = {
        "test_period": {
            "start": str(start_date),
            "end": str(end_date),
            "weeks": weeks,
        },
        "overall": overall_stats,
        "weekly": weekly_results,
        "predictions": all_evaluations[:100],  # Sample for review
        "generated_at": datetime.now().isoformat(),
    }
    
    storage = JSONStorage()
    storage.save(results, "data/evaluation/backtest_results.json")
    print("Results saved to: data/evaluation/backtest_results.json")


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="Run time-travel backtest")
    parser.add_argument("--weeks", type=int, default=10, help="Number of weeks to test")
    args = parser.parse_args()
    
    run_backtest(args.weeks)
