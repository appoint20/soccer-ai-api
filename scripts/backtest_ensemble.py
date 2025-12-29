#!/usr/bin/env python
"""
Ensemble backtesting script.

Uses all components:
- ML models (XGBoost/LightGBM)
- Dixon-Coles Poisson
- Ensemble predictor (weighted combination)
- Derby detection
- Odds filtering

Compares ensemble vs ML-only vs Poisson-only.
"""
import sys
from pathlib import Path
from datetime import datetime, date, timedelta
from typing import Dict, List, Any

sys.path.insert(0, str(Path(__file__).parent.parent))

from src.data.storage.json_storage import JSONStorage
from src.ml.ensemble.ensemble_predictor import EnsemblePredictor
from src.statistics.dixon_coles_model import DixonColesModel
from src.domain.services.prediction_service import PredictionService
from src.domain.services.derby_service import DerbyService
from src.domain.services.odds_filter_service import OddsFilterService
from src.utils.logger import get_logger

logger = get_logger("EnsembleBacktest")

# Tier mapping for leagues
TIER_MAP = {
    "E0": "tier1", "D1": "tier1", "I1": "tier1", "SP1": "tier1", "F1": "tier1",
    "E1": "tier2", "I2": "tier2", "F2": "tier2",
    "E2": "tier3", "E3": "tier3",
}


def load_matches() -> List[Dict]:
    """Load all historical matches."""
    storage = JSONStorage()
    matches = storage.load("data/processed/matches.json")
    if not matches:
        raise RuntimeError("No matches found. Run train_models.py first.")
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


def evaluate_prediction(pred: Dict, actual: Dict) -> Dict:
    """Compare prediction with actual result."""
    fthg = actual.get("fthg", 0) or 0
    ftag = actual.get("ftag", 0) or 0
    ftr = actual.get("ftr", "D")
    
    actual_over25 = (fthg + ftag) > 2.5
    actual_btts = fthg > 0 and ftag > 0
    
    return {
        "match": f"{actual.get('home_team')} vs {actual.get('away_team')}",
        "league": actual.get("league"),
        "over25": {
            "predicted": pred.get("over25", {}).get("prediction", "NO") == "YES",
            "actual": actual_over25,
            "correct": (pred.get("over25", {}).get("prediction", "NO") == "YES") == actual_over25,
            "prob": pred.get("over25", {}).get("probability", 0.5),
        },
        "btts": {
            "predicted": pred.get("btts", {}).get("prediction", "NO") == "YES",
            "actual": actual_btts,
            "correct": (pred.get("btts", {}).get("prediction", "NO") == "YES") == actual_btts,
            "prob": pred.get("btts", {}).get("probability", 0.5),
        },
        "result": {
            "predicted": pred.get("result", {}).get("prediction", "D"),
            "actual": ftr,
            "correct": pred.get("result", {}).get("prediction", "D") == ftr,
        },
    }


def run_ensemble_backtest(weeks: int = 10):
    """Run backtest with ensemble predictor."""
    print("=" * 60)
    print("Soccer GPT API - ENSEMBLE Backtest")
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
    start_date = latest - timedelta(weeks=weeks)
    print(f"  Test period: {start_date} to {latest}")
    
    # Initialize services
    print()
    print("[2/5] Initializing models...")
    
    derby_service = DerbyService()
    odds_filter = OddsFilterService()
    
    # Initialize ensemble predictors per tier
    ensembles = {}
    poissons = {}
    ml_services = {}
    
    # Get historical data (before test period)
    historical = filter_before_date(all_matches, start_date)
    print(f"  Historical matches for training: {len(historical)}")
    
    for tier in ["tier1", "tier2", "tier3"]:
        print(f"  Initializing {tier}...")
        
        # Ensemble
        ens = EnsemblePredictor(ml_weight=0.6, poisson_weight=0.4, tier=tier)
        ens.initialize(historical)
        ensembles[tier] = ens
        
        # Dixon-Coles only
        dc = DixonColesModel()
        dc.fit(historical)
        poissons[tier] = dc
        
        # ML only
        ml = PredictionService(tier=tier)
        ml.load_models(tier=tier)
        ml_services[tier] = ml
    
    print()
    print("[3/5] Running backtest...")
    
    # Track results for each method
    ensemble_results = []
    poisson_results = []
    ml_results = []
    derby_filtered = 0
    
    for week in range(weeks):
        week_start = start_date + timedelta(weeks=week)
        week_end = week_start + timedelta(days=6)
        
        week_matches = filter_by_date_range(all_matches, week_start, week_end)
        
        if not week_matches:
            continue
        
        week_ensemble = []
        week_poisson = []
        week_ml = []
        
        for match in week_matches:
            if match.get("fthg") is None or match.get("ftag") is None:
                continue
            
            home = match.get("home_team", "")
            away = match.get("away_team", "")
            league = match.get("league", "E0")
            tier = TIER_MAP.get(league, "tier1")
            
            # Check if derby (for stats)
            is_derby = derby_service.is_derby(home, away)
            if is_derby:
                derby_filtered += 1
            
            try:
                # Get match date for time-travel
                match_date = match.get("match_date")
                if isinstance(match_date, str):
                    match_date = datetime.fromisoformat(match_date[:10]).date()
                
                history = filter_before_date(all_matches, match_date)
                
                # Ensemble prediction
                ens_pred = ensembles[tier].predict_match(match, history)
                ens_eval = evaluate_prediction(ens_pred, match)
                week_ensemble.append(ens_eval)
                
                # Poisson-only prediction
                dc = poissons[tier]
                poisson_pred = {
                    "over25": {"prediction": "YES" if dc.predict_over25_prob(home, away) > 0.5 else "NO",
                               "probability": dc.predict_over25_prob(home, away)},
                    "btts": {"prediction": "YES" if dc.predict_btts_prob(home, away) > 0.5 else "NO",
                             "probability": dc.predict_btts_prob(home, away)},
                    "result": {"prediction": max(dc.predict_1x2(home, away), key=dc.predict_1x2(home, away).get).upper()[0]
                               if dc.predict_1x2(home, away) else "D"},
                }
                poisson_eval = evaluate_prediction(poisson_pred, match)
                week_poisson.append(poisson_eval)
                
                # ML-only prediction
                ml_pred = ml_services[tier].predict_match(match, history)
                ml_eval = evaluate_prediction(ml_pred, match)
                week_ml.append(ml_eval)
                
            except Exception as e:
                logger.error(f"Error processing match: {e}")
                continue
        
        ensemble_results.extend(week_ensemble)
        poisson_results.extend(week_poisson)
        ml_results.extend(week_ml)
        
        if week_ensemble:
            ens_o25 = sum(1 for e in week_ensemble if e["over25"]["correct"]) / len(week_ensemble)
            poi_o25 = sum(1 for e in week_poisson if e["over25"]["correct"]) / len(week_poisson)
            ml_o25 = sum(1 for e in week_ml if e["over25"]["correct"]) / len(week_ml)
            
            print(f"  Week {week + 1}: {len(week_ensemble)} matches | "
                  f"Ens: {ens_o25:.1%} | Poisson: {poi_o25:.1%} | ML: {ml_o25:.1%}")
    
    # Summary
    print()
    print("=" * 60)
    print("ENSEMBLE BACKTEST SUMMARY")
    print("=" * 60)
    print()
    
    n = len(ensemble_results)
    print(f"Total matches: {n}")
    print(f"Derby matches: {derby_filtered}")
    print()
    
    def calc_acc(results, key):
        return sum(1 for r in results if r[key]["correct"]) / len(results) if results else 0
    
    print("OVER 2.5 ACCURACY:")
    print(f"  Ensemble:    {calc_acc(ensemble_results, 'over25'):.1%}")
    print(f"  Dixon-Coles: {calc_acc(poisson_results, 'over25'):.1%}")
    print(f"  ML Only:     {calc_acc(ml_results, 'over25'):.1%}")
    print()
    
    print("BTTS ACCURACY:")
    print(f"  Ensemble:    {calc_acc(ensemble_results, 'btts'):.1%}")
    print(f"  Dixon-Coles: {calc_acc(poisson_results, 'btts'):.1%}")
    print(f"  ML Only:     {calc_acc(ml_results, 'btts'):.1%}")
    print()
    
    print("RESULT ACCURACY:")
    print(f"  Ensemble:    {calc_acc(ensemble_results, 'result'):.1%}")
    print(f"  Dixon-Coles: {calc_acc(poisson_results, 'result'):.1%}")
    print(f"  ML Only:     {calc_acc(ml_results, 'result'):.1%}")
    print()


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="Run ensemble backtest")
    parser.add_argument("--weeks", type=int, default=10, help="Weeks to test")
    args = parser.parse_args()
    
    run_ensemble_backtest(args.weeks)
