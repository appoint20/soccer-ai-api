#!/usr/bin/env python
"""
AI Prediction Backtest Script.

Time-travel backtesting with Gemini AI analysis:
1. For each week in test period, uses only historical data BEFORE that week
2. Runs full analysis including Gemini AI predictions
3. Compares AI predictions to actual results
4. Reports accuracy metrics

NO DATA LEAKAGE: Each prediction only sees past data.
"""
import sys
from pathlib import Path
from datetime import datetime, date, timedelta
from typing import Dict, List, Any, Optional
import json
from collections import defaultdict

sys.path.insert(0, str(Path(__file__).parent.parent))

from src.data.storage.json_storage import JSONStorage
from src.domain.services.ai_analysis_service import AIAnalysisService
from src.domain.services.calculators.team_form_calculator import TeamFormCalculator
from src.domain.services.calculators.h2h_stats_calculator import H2HStatsCalculator
from src.domain.services.calculators.poisson_goal_calculator import PoissonGoalCalculator
from src.domain.services.calculators.monte_carlo_uncertainty_adjuster import MonteCarloUncertaintyAdjuster
from src.domain.services.calculators.match_confidence_calculator import MatchConfidenceCalculator
from src.utils.logger import get_logger

logger = get_logger("AIBacktest")

LEAGUE_NAMES = {
    "E0": "Premier League",
    "E1": "Championship",
    "E2": "League One",
    "E3": "League Two",
    "D1": "Bundesliga",
    "SP1": "La Liga",
    "I1": "Serie A",
    "F1": "Ligue 1",
}


def load_matches() -> List[Dict]:
    """Load all historical matches."""
    storage = JSONStorage()
    matches = storage.load("data/processed/matches.json")
    if not matches:
        raise RuntimeError("No matches found. Run initial_data_load.py first.")
    return matches


def filter_matches_by_date_range(
    matches: List[Dict],
    start_date: date,
    end_date: date,
) -> List[Dict]:
    """Filter matches within a date range."""
    result = []
    for m in matches:
        match_date = parse_date(m.get("match_date"))
        if match_date and start_date <= match_date <= end_date:
            result.append(m)
    return result


def filter_matches_before_date(
    matches: List[Dict],
    cutoff_date: date,
) -> List[Dict]:
    """Filter matches strictly before a date (for training/history)."""
    result = []
    for m in matches:
        match_date = parse_date(m.get("match_date"))
        if match_date and match_date < cutoff_date:
            result.append(m)
    return result


def parse_date(date_val) -> Optional[date]:
    """Parse date from various formats."""
    if not date_val:
        return None
    if isinstance(date_val, date):
        return date_val
    if isinstance(date_val, datetime):
        return date_val.date()
    if isinstance(date_val, str):
        try:
            return datetime.fromisoformat(date_val[:10]).date()
        except:
            return None
    return None


def analyze_match_with_time_travel(
    match: Dict,
    historical_matches: List[Dict],
    calculators: Dict,
    ai_service: AIAnalysisService,
    league: str,
) -> Dict:
    """
    Analyze a match using only data before its date.
    Returns analysis with AI prediction.
    """
    home_team = match.get("home_team") or match.get("HomeTeam")
    away_team = match.get("away_team") or match.get("AwayTeam")
    
    # 1. Calculate form stats
    home_last_5 = calculators["form"].calculate_form_stats(
        team=home_team,
        matches=historical_matches,
        last_n=5,
        venue_filter=None,
    )
    
    away_last_5 = calculators["form"].calculate_form_stats(
        team=away_team,
        matches=historical_matches,
        last_n=5,
        venue_filter=None,
    )
    
    # 2. H2H stats
    h2h_stats = calculators["h2h"].calculate_h2h_stats(
        home_team=home_team,
        away_team=away_team,
        matches=historical_matches,
        last_n=5,
    )
    
    # 3. Poisson probabilities
    poisson = calculators["poisson"].calculate_probabilities(
        home_team=home_team,
        away_team=away_team,
        home_stats=home_last_5,
        away_stats=away_last_5,
        league_code=league,
        league_avg_goals=2.7,
    )
    
    # 4. Confidence
    mc_results = calculators["mc"].calculate_all_markets(
        poisson_probs={
            "over_25": poisson.over_25,
            "btts": poisson.btts,
            "home_win": poisson.home_win,
            "away_win": poisson.away_win,
            "draw": poisson.draw,
            "goals_2_3": poisson.goals_2_3,
        },
        recent_outcomes={},
    )
    
    confidence = calculators["confidence"].calculate_confidence_index(
        home_stats=home_last_5,
        away_stats=away_last_5,
        h2h_stats=h2h_stats,
        poisson_probs=poisson,
        mc_results=mc_results,
        league_code=league,
    )
    
    # Build match data for AI
    match_data = {
        "match_id": f"{match.get('match_date')}_{home_team}_vs_{away_team}",
        "home_team": home_team,
        "away_team": away_team,
        "home_last_5": home_last_5.to_dict(),
        "away_last_5": away_last_5.to_dict(),
        "h2h_last_5": h2h_stats.to_dict(),
        "poisson": poisson.to_dict(),
        "overall_confidence": confidence,
    }
    
    # 5. Get AI prediction
    ai_analysis = None
    try:
        ai_results = ai_service.analyze_matches_batch(
            matches=[match_data],
            league=LEAGUE_NAMES.get(league, league),
        )
        if match_data["match_id"] in ai_results:
            ai_analysis = ai_results[match_data["match_id"]]
    except Exception as e:
        logger.warning(f"AI analysis failed: {e}")
    
    return {
        "match_id": match_data["match_id"],
        "home_team": home_team,
        "away_team": away_team,
        "date": str(match.get("match_date", ""))[:10],
        "league": league,
        "poisson": {
            "home_win": poisson.home_win,
            "draw": poisson.draw,
            "away_win": poisson.away_win,
            "over_25": poisson.over_25,
            "btts": poisson.btts,
        },
        "confidence": confidence,
        "ai_analysis": {
            "best_prediction": ai_analysis.best_prediction if ai_analysis else "NO_AI",
            "reason": ai_analysis.reason if ai_analysis else "",
            "confidence_level": ai_analysis.confidence_level if ai_analysis else "NONE",
        } if ai_analysis else None,
    }


def evaluate_ai_prediction(analysis: Dict, actual: Dict) -> Dict:
    """
    Evaluate AI prediction against actual result.
    """
    fthg = int(actual.get("fthg") or actual.get("FTHG") or 0)
    ftag = int(actual.get("ftag") or actual.get("FTAG") or 0)
    ftr = actual.get("ftr") or actual.get("FTR") or "D"
    
    total_goals = fthg + ftag
    
    actual_result = {
        "home_win": ftr == "H",
        "draw": ftr == "D",
        "away_win": ftr == "A",
        "over_25": total_goals > 2.5,
        "btts": fthg > 0 and ftag > 0,
        "goals_2_3": total_goals in [2, 3],
        "ftr": ftr,
        "score": f"{fthg}-{ftag}",
    }
    
    result = {
        "match": f"{analysis['home_team']} vs {analysis['away_team']}",
        "date": analysis.get("date"),
        "league": analysis.get("league"),
        "actual": actual_result,
        "poisson": analysis.get("poisson", {}),
    }
    
    # Evaluate AI prediction
    ai = analysis.get("ai_analysis")
    if ai and ai.get("best_prediction"):
        pred = ai["best_prediction"].upper()
        
        # Map prediction to actual
        correct = False
        if "HOME" in pred or pred == "H":
            correct = actual_result["home_win"]
        elif "AWAY" in pred or pred == "A":
            correct = actual_result["away_win"]
        elif "DRAW" in pred or pred == "D":
            correct = actual_result["draw"]
        elif "OVER 2.5" in pred or "O25" in pred:
            correct = actual_result["over_25"]
        elif "UNDER 2.5" in pred or "U25" in pred:
            correct = not actual_result["over_25"]
        elif "BTTS" in pred and "NO" not in pred:
            correct = actual_result["btts"]
        elif "BTTS NO" in pred or "BTTS: NO" in pred:
            correct = not actual_result["btts"]
        elif "2-3 GOALS" in pred or "GOALS 2-3" in pred:
            correct = actual_result["goals_2_3"]
        elif "NO BET" in pred or "SKIP" in pred:
            correct = None  # No prediction made
        
        result["ai_prediction"] = {
            "prediction": pred,
            "reason": ai.get("reason", ""),
            "confidence": ai.get("confidence_level", "LOW"),
            "correct": correct,
        }
    else:
        result["ai_prediction"] = None
    
    return result


def run_ai_backtest(weeks: int = 10, sample_per_week: int = 20):
    """
    Run AI prediction backtest for the last N weeks.
    
    Args:
        weeks: Number of weeks to test
        sample_per_week: Max matches per week (to limit API calls)
    """
    print("=" * 70)
    print("Soccer GPT API - AI Prediction Backtest (Time-Travel)")
    print("=" * 70)
    print()
    
    # Load all matches
    print("[1/4] Loading data...")
    all_matches = load_matches()
    print(f"  Loaded {len(all_matches)} total matches")
    
    # Find date range
    dates = [parse_date(m.get("match_date")) for m in all_matches]
    dates = [d for d in dates if d]
    
    if not dates:
        print("  ERROR: No valid dates")
        return
    
    latest_date = max(dates)
    earliest_date = min(dates)
    print(f"  Date range: {earliest_date} to {latest_date}")
    
    # Calculate test period
    end_date = latest_date
    start_date = end_date - timedelta(weeks=weeks)
    print(f"  Testing period: {start_date} to {end_date} ({weeks} weeks)")
    print()
    
    # Initialize services
    print("[2/4] Initializing services...")
    calculators = {
        "form": TeamFormCalculator(),
        "h2h": H2HStatsCalculator(),
        "poisson": PoissonGoalCalculator(),
        "mc": MonteCarloUncertaintyAdjuster(),
        "confidence": MatchConfidenceCalculator(),
    }
    
    ai_service = AIAnalysisService()
    print("  ✓ Calculators initialized")
    print("  ✓ AI Service initialized (Gemini)")
    print()
    
    # Run backtest
    print("[3/4] Running backtest with AI predictions...")
    print()
    
    all_evaluations = []
    weekly_stats = []
    
    for week_num in range(weeks):
        week_start = start_date + timedelta(weeks=week_num)
        week_end = week_start + timedelta(days=6)
        
        # Get matches for this week
        week_matches = filter_matches_by_date_range(all_matches, week_start, week_end)
        
        # Filter to only completed matches
        week_matches = [m for m in week_matches if m.get("fthg") is not None or m.get("FTHG") is not None]
        
        if not week_matches:
            print(f"  Week {week_num + 1} ({week_start}): No matches")
            continue
        
        # Sample if too many matches
        if len(week_matches) > sample_per_week:
            import random
            week_matches = random.sample(week_matches, sample_per_week)
        
        print(f"  Week {week_num + 1} ({week_start}): Analyzing {len(week_matches)} matches...")
        
        week_evaluations = []
        
        for match in week_matches:
            match_date = parse_date(match.get("match_date"))
            if not match_date:
                continue
            
            # TIME TRAVEL: Only use data BEFORE this match
            history = filter_matches_before_date(all_matches, match_date)
            
            if len(history) < 100:
                continue  # Not enough history
            
            try:
                league = match.get("league") or match.get("Div") or "E0"
                
                # Analyze with time-travel
                analysis = analyze_match_with_time_travel(
                    match=match,
                    historical_matches=history,
                    calculators=calculators,
                    ai_service=ai_service,
                    league=league,
                )
                
                # Evaluate prediction
                evaluation = evaluate_ai_prediction(analysis, match)
                week_evaluations.append(evaluation)
                all_evaluations.append(evaluation)
                
            except Exception as e:
                logger.error(f"Error processing match: {e}")
                continue
        
        # Calculate week stats
        ai_preds = [e for e in week_evaluations if e.get("ai_prediction")]
        ai_correct = sum(1 for e in ai_preds if e["ai_prediction"].get("correct") == True)
        ai_total = len([e for e in ai_preds if e["ai_prediction"].get("correct") is not None])
        
        week_stat = {
            "week": week_num + 1,
            "start_date": str(week_start),
            "total_matches": len(week_evaluations),
            "ai_predictions": ai_total,
            "ai_correct": ai_correct,
            "ai_accuracy": ai_correct / ai_total if ai_total > 0 else 0,
        }
        weekly_stats.append(week_stat)
        
        if ai_total > 0:
            print(f"    ↳ AI Accuracy: {ai_correct}/{ai_total} ({week_stat['ai_accuracy']:.1%})")
    
    print()
    
    # Summary
    print("[4/4] Results Summary")
    print("=" * 70)
    
    total_matches = len(all_evaluations)
    ai_predictions = [e for e in all_evaluations if e.get("ai_prediction")]
    ai_with_result = [e for e in ai_predictions if e["ai_prediction"].get("correct") is not None]
    ai_correct = sum(1 for e in ai_with_result if e["ai_prediction"]["correct"])
    
    # By confidence level
    high_conf = [e for e in ai_with_result if e["ai_prediction"].get("confidence") == "HIGH"]
    high_conf_correct = sum(1 for e in high_conf if e["ai_prediction"]["correct"])
    
    med_conf = [e for e in ai_with_result if e["ai_prediction"].get("confidence") == "MEDIUM"]
    med_conf_correct = sum(1 for e in med_conf if e["ai_prediction"]["correct"])
    
    print(f"\nTotal matches tested: {total_matches}")
    print(f"AI predictions made: {len(ai_with_result)}")
    print()
    print("🎯 OVERALL AI ACCURACY:")
    print(f"   {ai_correct}/{len(ai_with_result)} = {ai_correct/len(ai_with_result):.1%}" if ai_with_result else "   No predictions")
    print()
    print("📊 BY CONFIDENCE LEVEL:")
    print(f"   HIGH:   {high_conf_correct}/{len(high_conf)} = {high_conf_correct/len(high_conf):.1%}" if high_conf else "   HIGH:   No predictions")
    print(f"   MEDIUM: {med_conf_correct}/{len(med_conf)} = {med_conf_correct/len(med_conf):.1%}" if med_conf else "   MEDIUM: No predictions")
    print()
    
    # By prediction type
    pred_types = defaultdict(lambda: {"correct": 0, "total": 0})
    for e in ai_with_result:
        pred = e["ai_prediction"]["prediction"]
        # Normalize
        if "HOME" in pred or pred == "H":
            key = "HOME WIN"
        elif "AWAY" in pred or pred == "A":
            key = "AWAY WIN"
        elif "DRAW" in pred or pred == "D":
            key = "DRAW"
        elif "OVER 2.5" in pred:
            key = "OVER 2.5"
        elif "BTTS" in pred and "NO" not in pred:
            key = "BTTS YES"
        else:
            key = "OTHER"
        
        pred_types[key]["total"] += 1
        if e["ai_prediction"]["correct"]:
            pred_types[key]["correct"] += 1
    
    print("📈 BY PREDICTION TYPE:")
    for pred_type, stats in sorted(pred_types.items(), key=lambda x: -x[1]["total"]):
        acc = stats["correct"] / stats["total"] if stats["total"] > 0 else 0
        print(f"   {pred_type:12s}: {stats['correct']}/{stats['total']} = {acc:.1%}")
    
    print()
    
    # Save results
    results = {
        "test_period": {
            "start": str(start_date),
            "end": str(end_date),
            "weeks": weeks,
        },
        "summary": {
            "total_matches": total_matches,
            "ai_predictions": len(ai_with_result),
            "ai_correct": ai_correct,
            "ai_accuracy": ai_correct / len(ai_with_result) if ai_with_result else 0,
            "high_confidence_accuracy": high_conf_correct / len(high_conf) if high_conf else 0,
        },
        "weekly": weekly_stats,
        "by_type": dict(pred_types),
        "sample_predictions": all_evaluations[:50],
        "generated_at": datetime.now().isoformat(),
    }
    
    storage = JSONStorage()
    output_path = "data/evaluation/ai_backtest_results.json"
    storage.save(results, output_path)
    print(f"Results saved to: {output_path}")


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="Run AI prediction backtest")
    parser.add_argument("--weeks", type=int, default=10, help="Number of weeks to test")
    parser.add_argument("--sample", type=int, default=20, help="Max matches per week")
    args = parser.parse_args()
    
    run_ai_backtest(weeks=args.weeks, sample_per_week=args.sample)
