#!/usr/bin/env python
"""
Optimized Dixon-Coles backtesting with:
- Parameter tuning (xi, rho grid search)
- High-confidence filtering (only >60% predictions)
- Derby exclusion option
"""
import sys
from pathlib import Path
from datetime import datetime, date, timedelta
from typing import Dict, List, Any, Tuple

sys.path.insert(0, str(Path(__file__).parent.parent))

from src.data.storage.json_storage import JSONStorage
from src.statistics.dixon_coles_model import DixonColesModel
from src.domain.services.derby_service import DerbyService
from src.utils.logger import get_logger

logger = get_logger("OptimizedBacktest")


def load_matches() -> List[Dict]:
    """Load all historical matches."""
    storage = JSONStorage()
    matches = storage.load("data/processed/matches.json")
    if not matches:
        raise RuntimeError("No matches found.")
    return matches


def filter_by_date_range(matches: List[Dict], start: date, end: date) -> List[Dict]:
    """Filter matches in date range."""
    result = []
    for m in matches:
        d = m.get("match_date")
        if d:
            if isinstance(d, str):
                d = datetime.fromisoformat(d[:10]).date()
            elif isinstance(d, datetime):
                d = d.date()
            if start <= d <= end:
                result.append(m)
    return result


def filter_before_date(matches: List[Dict], cutoff: date) -> List[Dict]:
    """Filter matches before a date."""
    result = []
    for m in matches:
        d = m.get("match_date")
        if d:
            if isinstance(d, str):
                d = datetime.fromisoformat(d[:10]).date()
            elif isinstance(d, datetime):
                d = d.date()
            if d < cutoff:
                result.append(m)
    return result


def evaluate_dixon_coles(
    model: DixonColesModel,
    matches: List[Dict],
    all_matches: List[Dict],
    derby_service: DerbyService,
    confidence_threshold: float = 0.0,
    exclude_derbies: bool = False,
) -> Dict[str, Any]:
    """
    Evaluate Dixon-Coles predictions on matches.
    
    Args:
        model: Fitted Dixon-Coles model
        matches: Test matches
        all_matches: All matches for time-travel
        derby_service: Derby detection
        confidence_threshold: Minimum probability to make prediction
        exclude_derbies: Whether to skip derby matches
        
    Returns:
        Accuracy metrics
    """
    over25_correct = 0
    over25_total = 0
    btts_correct = 0
    btts_total = 0
    result_correct = 0
    result_total = 0
    skipped = 0
    derby_skipped = 0
    
    for match in matches:
        home = match.get("home_team", "")
        away = match.get("away_team", "")
        fthg = match.get("fthg")
        ftag = match.get("ftag")
        ftr = match.get("ftr", "D")
        
        if fthg is None or ftag is None:
            continue
        
        # Check derby
        is_derby = derby_service.is_derby(home, away)
        if exclude_derbies and is_derby:
            derby_skipped += 1
            continue
        
        # Get predictions
        over25_prob = model.predict_over25_prob(home, away)
        btts_prob = model.predict_btts_prob(home, away)
        result_probs = model.predict_1x2(home, away)
        
        # Actual outcomes
        actual_over25 = (fthg + ftag) > 2.5
        actual_btts = fthg > 0 and ftag > 0
        
        # Over 2.5 evaluation with confidence filter
        prob_distance = abs(over25_prob - 0.5)
        if prob_distance >= (confidence_threshold - 0.5):  # e.g., 0.6 -> 0.1 distance
            over25_total += 1
            pred_over25 = over25_prob > 0.5
            if pred_over25 == actual_over25:
                over25_correct += 1
        else:
            skipped += 1
        
        # BTTS evaluation
        btts_distance = abs(btts_prob - 0.5)
        if btts_distance >= (confidence_threshold - 0.5):
            btts_total += 1
            pred_btts = btts_prob > 0.5
            if pred_btts == actual_btts:
                btts_correct += 1
        
        # Result evaluation (always take max prob)
        max_prob = max(result_probs.values())
        if max_prob >= confidence_threshold or confidence_threshold == 0:
            result_total += 1
            pred_result = max(result_probs, key=result_probs.get)
            # Map to H/D/A
            pred_result = pred_result[0].upper() if pred_result.startswith(("home", "draw", "away")) else pred_result
            if pred_result == "home_win":
                pred_result = "H"
            elif pred_result == "away_win":
                pred_result = "A"
            elif pred_result == "draw":
                pred_result = "D"
            
            if pred_result == ftr:
                result_correct += 1
    
    return {
        "over25": {
            "accuracy": over25_correct / over25_total if over25_total else 0,
            "correct": over25_correct,
            "total": over25_total,
        },
        "btts": {
            "accuracy": btts_correct / btts_total if btts_total else 0,
            "correct": btts_correct,
            "total": btts_total,
        },
        "result": {
            "accuracy": result_correct / result_total if result_total else 0,
            "correct": result_correct,
            "total": result_total,
        },
        "skipped": skipped,
        "derby_skipped": derby_skipped,
    }


def grid_search_params(
    train_matches: List[Dict],
    val_matches: List[Dict],
    all_matches: List[Dict],
    derby_service: DerbyService,
) -> Tuple[float, float, Dict]:
    """
    Grid search for optimal xi and rho parameters.
    
    Returns:
        (best_xi, best_rho, results)
    """
    print("\n[Grid Search] Testing parameter combinations...")
    
    xi_values = [0.003, 0.005, 0.007, 0.01, 0.015]
    rho_values = [-0.05, -0.10, -0.13, -0.15, -0.20]
    
    best_score = 0
    best_xi = 0.005
    best_rho = -0.13
    results = []
    
    for xi in xi_values:
        for rho in rho_values:
            model = DixonColesModel(xi=xi, rho=rho)
            model.fit(train_matches)
            
            eval_result = evaluate_dixon_coles(
                model, val_matches, all_matches, derby_service,
                confidence_threshold=0.0, exclude_derbies=False
            )
            
            # Score = average of over25, btts, result accuracy
            score = (
                eval_result["over25"]["accuracy"] +
                eval_result["btts"]["accuracy"] +
                eval_result["result"]["accuracy"]
            ) / 3
            
            results.append({
                "xi": xi,
                "rho": rho,
                "score": score,
                "over25": eval_result["over25"]["accuracy"],
                "btts": eval_result["btts"]["accuracy"],
                "result": eval_result["result"]["accuracy"],
            })
            
            if score > best_score:
                best_score = score
                best_xi = xi
                best_rho = rho
    
    print(f"[Grid Search] Best params: xi={best_xi}, rho={best_rho} (score={best_score:.1%})")
    
    return best_xi, best_rho, results


def run_optimized_backtest(weeks: int = 10):
    """Run optimized Dixon-Coles backtest."""
    print("=" * 60)
    print("OPTIMIZED DIXON-COLES BACKTEST")
    print("=" * 60)
    print()
    
    # Load data
    print("[1/5] Loading data...")
    all_matches = load_matches()
    print(f"  Loaded {len(all_matches)} matches")
    
    # Get date range
    dates = []
    for m in all_matches:
        d = m.get("match_date")
        if d:
            if isinstance(d, str):
                d = datetime.fromisoformat(d[:10]).date()
            dates.append(d)
    
    latest = max(dates)
    test_start = latest - timedelta(weeks=weeks)
    val_start = test_start - timedelta(weeks=4)  # 4 weeks for validation
    
    print(f"  Validation: {val_start} to {test_start}")
    print(f"  Test: {test_start} to {latest}")
    
    # Split data
    train_matches = filter_before_date(all_matches, val_start)
    val_matches = filter_by_date_range(all_matches, val_start, test_start - timedelta(days=1))
    test_matches = filter_by_date_range(all_matches, test_start, latest)
    
    print(f"  Train: {len(train_matches)}, Val: {len(val_matches)}, Test: {len(test_matches)}")
    
    derby_service = DerbyService()
    
    # Grid search for optimal parameters
    print()
    print("[2/5] Parameter tuning...")
    best_xi, best_rho, grid_results = grid_search_params(
        train_matches, val_matches, all_matches, derby_service
    )
    
    # Fit model with best params
    print()
    print("[3/5] Fitting optimized model...")
    optimized_model = DixonColesModel(xi=best_xi, rho=best_rho)
    
    # Fit on train + val for final test
    combined_train = filter_before_date(all_matches, test_start)
    optimized_model.fit(combined_train)
    
    # Baseline model (default params)
    baseline_model = DixonColesModel(xi=0.005, rho=-0.13)
    baseline_model.fit(combined_train)
    
    # Evaluate
    print()
    print("[4/5] Running comparisons...")
    
    # 1. Baseline (default params, no filters)
    baseline_results = evaluate_dixon_coles(
        baseline_model, test_matches, all_matches, derby_service,
        confidence_threshold=0.0, exclude_derbies=False
    )
    
    # 2. Optimized params only
    optimized_results = evaluate_dixon_coles(
        optimized_model, test_matches, all_matches, derby_service,
        confidence_threshold=0.0, exclude_derbies=False
    )
    
    # 3. Optimized + confidence filter (>55%)
    filtered_55 = evaluate_dixon_coles(
        optimized_model, test_matches, all_matches, derby_service,
        confidence_threshold=0.55, exclude_derbies=False
    )
    
    # 4. Optimized + confidence filter (>60%)
    filtered_60 = evaluate_dixon_coles(
        optimized_model, test_matches, all_matches, derby_service,
        confidence_threshold=0.60, exclude_derbies=False
    )
    
    # 5. Optimized + confidence + no derbies
    no_derby = evaluate_dixon_coles(
        optimized_model, test_matches, all_matches, derby_service,
        confidence_threshold=0.55, exclude_derbies=True
    )
    
    # Results
    print()
    print("=" * 60)
    print("RESULTS COMPARISON")
    print("=" * 60)
    print()
    
    print("OVER 2.5 GOALS:")
    print(f"  Baseline (default):      {baseline_results['over25']['accuracy']:.1%} ({baseline_results['over25']['total']} matches)")
    print(f"  Optimized params:        {optimized_results['over25']['accuracy']:.1%} ({optimized_results['over25']['total']} matches)")
    print(f"  + Confidence >55%:       {filtered_55['over25']['accuracy']:.1%} ({filtered_55['over25']['total']} matches)")
    print(f"  + Confidence >60%:       {filtered_60['over25']['accuracy']:.1%} ({filtered_60['over25']['total']} matches)")
    print(f"  + No derbies (>55%):     {no_derby['over25']['accuracy']:.1%} ({no_derby['over25']['total']} matches)")
    print()
    
    print("BTTS:")
    print(f"  Baseline (default):      {baseline_results['btts']['accuracy']:.1%} ({baseline_results['btts']['total']} matches)")
    print(f"  Optimized params:        {optimized_results['btts']['accuracy']:.1%} ({optimized_results['btts']['total']} matches)")
    print(f"  + Confidence >55%:       {filtered_55['btts']['accuracy']:.1%} ({filtered_55['btts']['total']} matches)")
    print(f"  + Confidence >60%:       {filtered_60['btts']['accuracy']:.1%} ({filtered_60['btts']['total']} matches)")
    print(f"  + No derbies (>55%):     {no_derby['btts']['accuracy']:.1%} ({no_derby['btts']['total']} matches)")
    print()
    
    print("RESULT (1X2):")
    print(f"  Baseline (default):      {baseline_results['result']['accuracy']:.1%} ({baseline_results['result']['total']} matches)")
    print(f"  Optimized params:        {optimized_results['result']['accuracy']:.1%} ({optimized_results['result']['total']} matches)")
    print(f"  + Confidence >55%:       {filtered_55['result']['accuracy']:.1%} ({filtered_55['result']['total']} matches)")
    print(f"  + Confidence >60%:       {filtered_60['result']['accuracy']:.1%} ({filtered_60['result']['total']} matches)")
    print(f"  + No derbies (>55%):     {no_derby['result']['accuracy']:.1%} ({no_derby['result']['total']} matches)")
    print()
    
    print(f"Best parameters found: xi={best_xi}, rho={best_rho}")
    print(f"Derbies skipped: {no_derby['derby_skipped']}")


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="Run optimized backtest")
    parser.add_argument("--weeks", type=int, default=10, help="Test weeks")
    args = parser.parse_args()
    
    run_optimized_backtest(args.weeks)
