"""
ML Model Failure Analysis
Analyzes the 30% wrong predictions to identify patterns and improvements
"""
import pandas as pd
import numpy as np
from pathlib import Path
from collections import Counter
import sys

PROJECT_ROOT = Path(__file__).parent.parent
sys.path.insert(0, str(PROJECT_ROOT))

from scripts.enhanced_backtest import (
    load_historical_matches, load_all_historical_data, build_h2h_cache,
    calculate_historical_stats, get_h2h_features
)
from app.core.ml_predictor import get_ml_predictor
from app.core.fixture_congestion import calculate_fixture_congestion
from app.core.poisson import PoissonPredictor
from app.core.monte_carlo import MonteCarloSimulator

DATA_DIR = PROJECT_ROOT / "data"
REPORT_DIR = Path.home() / ".gemini" / "antigravity" / "brain" / "3d63d768-8366-4689-9f50-05ba8a1be4a6"


def analyze_ml_failures(weeks=10):
    """Analyze ML model failures to find patterns."""
    
    print("=" * 70)
    print("ML MODEL FAILURE ANALYSIS")
    print("=" * 70)
    
    # Load data
    matches_df = load_historical_matches(weeks)
    all_historical_df = load_all_historical_data()
    h2h_cache = build_h2h_cache(all_historical_df)
    # Initialize predictors
    ml_predictor = get_ml_predictor()
    monte_carlo = MonteCarloSimulator()
    poisson = PoissonPredictor()
    
    print(f"\nAnalyzing {len(matches_df)} matches...")
    
    failures = {
        'predicted_home_actual_draw': [],
        'predicted_home_actual_away': [],
        'predicted_draw_actual_home': [],
        'predicted_draw_actual_away': [],
        'predicted_away_actual_home': [],
        'predicted_away_actual_draw': [],
    }
    
    failure_details = []
    
    for idx, row in matches_df.iterrows():
        home_team = row['HomeTeam']
        away_team = row['AwayTeam']
        league_id = row['League']
        match_date = row['Date']
        actual_result = row['FTR']
        
        # Get stats
        home_stats = calculate_historical_stats(home_team, league_id, match_date, all_historical_df)
        away_stats = calculate_historical_stats(away_team, league_id, match_date, all_historical_df)
        h2h_features = get_h2h_features(home_team, away_team, h2h_cache)
        
        odds = {
            'home': row.get('B365H', 2.0) or 2.0,
            'draw': row.get('B365D', 3.3) or 3.3,
            'away': row.get('B365A', 3.5) or 3.5
        }
        
        # Get congestion
        home_congestion = calculate_fixture_congestion(home_team, league_id, match_date, all_historical_df)
        away_congestion = calculate_fixture_congestion(away_team, league_id, match_date, all_historical_df)

        # Get ML prediction
        ml_result = ml_predictor.predict(home_stats, away_stats, odds, h2h_features, congestion={
             'home_congestion_index': home_congestion.get('congestion_index', 0),
             'away_congestion_index': away_congestion.get('congestion_index', 0),
        })
        predicted = ml_result['prediction']
        confidence = ml_result['confidence']
        
        # Get MC and Poisson predictions for comparison
        mc_result = monte_carlo.simulate(
            home_attack=home_stats.get('avg_goals_scored', 1.3),
            home_defense=home_stats.get('avg_goals_conceded', 1.1),
            away_attack=away_stats.get('avg_goals_scored', 1.1),
            away_defense=away_stats.get('avg_goals_conceded', 1.3)
        )
        poisson_result = poisson.predict(
            home_attack=home_stats.get('avg_goals_scored', 1.3),
            home_defense=home_stats.get('avg_goals_conceded', 1.1),
            away_attack=away_stats.get('avg_goals_scored', 1.1),
            away_defense=away_stats.get('avg_goals_conceded', 1.3)
        )
        
        # Convert MC/Poisson results to standard format
        mc_prediction = mc_result.get('hdw', 'N/A')
        mc_confidence = mc_result.get('hdw_confidence', 0)
        
        # Extract MC BTTS and O2.5 predictions
        mc_btts_prob = mc_result.get('btts_probability', 0.5)
        mc_btts_prediction = "Yes" if mc_btts_prob > 0.5 else "No"
        
        mc_over25_prob = mc_result.get('over_25_probability', 0.5)
        mc_over25_prediction = "Over" if mc_over25_prob > 0.5 else "Under"
        
        poisson_prediction = poisson_result.get('prediction', 'N/A')
        poisson_confidence = poisson_result.get('confidence', 0)
        
        if predicted != actual_result:
            # Categorize failure
            key = f"predicted_{predicted.lower()}_actual_{actual_result.lower()}"
            key_map = {
                'predicted_h_actual_d': 'predicted_home_actual_draw',
                'predicted_h_actual_a': 'predicted_home_actual_away',
                'predicted_d_actual_h': 'predicted_draw_actual_home',
                'predicted_d_actual_a': 'predicted_draw_actual_away',
                'predicted_a_actual_h': 'predicted_away_actual_home',
                'predicted_a_actual_d': 'predicted_away_actual_draw',
            }
            
            category = key_map.get(key, 'other')
            failures[category].append({
                'match': f"{home_team} vs {away_team}",
                'home_team': home_team,
                'away_team': away_team,
                'predicted': predicted,
                'actual': actual_result,
                'confidence': confidence,
                'odds_home': odds['home'],
                'odds_draw': odds['draw'],
                'odds_away': odds['away'],
                'home_congestion': home_congestion['congestion_index'],
                'away_congestion': away_congestion['congestion_index'],
                'h2h_matches': h2h_features.get('matches', 0),
                'date': match_date.strftime('%Y-%m-%d'),
                # Add MC and Poisson predictions
                'mc_prediction': mc_prediction,
                'mc_confidence': mc_confidence,
                'poisson_prediction': poisson_prediction,
                'poisson_confidence': poisson_confidence,
                'mc_agreed': mc_prediction == actual_result,
                'poisson_agreed': poisson_prediction == actual_result
            })
            
        # BTTS Check
        actual_home_goals = row['FTHG']
        actual_away_goals = row['FTAG']
        actual_btts = "Yes" if (actual_home_goals > 0 and actual_away_goals > 0) else "No"
        
        btts_pred = ml_result.get('btts', {}).get('prediction', 'Skip')
        if btts_pred != 'Skip' and btts_pred != actual_btts:
            failures.setdefault('btts_failures', []).append({
                'match': f"{home_team} vs {away_team}",
                'home_team': home_team,
                'away_team': away_team,
                'date': match_date.strftime('%Y-%m-%d'),
                'predicted': btts_pred,
                'actual': actual_btts,
                'confidence': ml_result['btts'].get('confidence', 0),
                'score': f"{int(actual_home_goals)}-{int(actual_away_goals)}",
                'home_congestion': home_congestion['congestion_index'],
                'away_congestion': away_congestion['congestion_index'],
                'h2h_matches': h2h_features.get('matches', 0),
                'odds_home': odds['home'],
                'odds_away': odds['away'],
                # MC comparison
                'mc_btts_prediction': mc_btts_prediction,
                'mc_btts_agreed': mc_btts_prediction == actual_btts
            })
            
        # Over 2.5 Check
        total_goals = actual_home_goals + actual_away_goals
        actual_o25 = "Over" if total_goals > 2.5 else "Under"
        
        o25_pred = ml_result.get('over_25', {}).get('prediction', 'Skip')
        if o25_pred != 'Skip' and o25_pred != actual_o25:
            failures.setdefault('over25_failures', []).append({
                'match': f"{home_team} vs {away_team}",
                'home_team': home_team,
                'away_team': away_team,
                'date': match_date.strftime('%Y-%m-%d'),
                'predicted': o25_pred,
                'actual': actual_o25,
                'confidence': ml_result['over_25'].get('confidence', 0),
                'score': f"{int(actual_home_goals)}-{int(actual_away_goals)}",
                'home_congestion': home_congestion['congestion_index'],
                'away_congestion': away_congestion['congestion_index'],
                'h2h_matches': h2h_features.get('matches', 0),
                'odds_home': odds['home'],
                'odds_away': odds['away'],
                # MC comparison
                'mc_over25_prediction': mc_over25_prediction,
                'mc_over25_agreed': mc_over25_prediction == actual_o25
            })
    
    # Analysis
    print("\n" + "=" * 70)
    print("FAILURE BREAKDOWN")
    print("=" * 70)
    
    total_failures = sum(len(v) for v in failures.values())
    total_matches = len(matches_df)
    
    print(f"\nTotal Failures: {total_failures}/{total_matches} ({total_failures/total_matches*100:.1f}%)")
    print(f"\nFailure Categories:\n")
    
    for category, matches in failures.items():
        if matches:
            count = len(matches)
            pct = count / total_failures * 100
            avg_conf = np.mean([m['confidence'] for m in matches])
            print(f"  {category:40} {count:3} ({pct:5.1f}%) - Avg Confidence: {avg_conf:.1%}")
    
    # Deep dive into biggest category
    biggest_category = max(failures.items(), key=lambda x: len(x[1]))
    cat_name, cat_matches = biggest_category
    
    print(f"\n" + "=" * 70)
    print(f"DEEP DIVE: {cat_name.upper()} ({len(cat_matches)} cases)")
    print("=" * 70)
    
    # Analyze patterns
    high_conf_failures = [m for m in cat_matches if m['confidence'] > 0.7]
    print(f"\nHigh Confidence Failures (>70%): {len(high_conf_failures)}")
    
    congested_teams = [m for m in cat_matches if m['home_congestion'] >= 3 or m['away_congestion'] >= 3]
    print(f"Fixture Congestion Factor: {len(congested_teams)} ({len(congested_teams)/len(cat_matches)*100:.1f}%)")
    
    poor_h2h = [m for m in cat_matches if m['h2h_matches'] < 3]
    print(f"Limited H2H Data (<3 matches): {len(poor_h2h)} ({len(poor_h2h)/len(cat_matches)*100:.1f}%)")
    
    # Odds analysis
    odds_surprises = []
    for m in cat_matches:
        if m['predicted'] == 'H' and m['odds_home'] > 2.5:
            odds_surprises.append(m)
        elif m['predicted'] == 'A' and m['odds_away'] > 2.5:
            odds_surprises.append(m)
    
    print(f"Odds Surprises (predicted favorite but odds > 2.5): {len(odds_surprises)}")
    
    # Generate recommendations
    print("\n" + "=" * 70)
    print("IMPROVEMENT RECOMMENDATIONS")
    print("=" * 70)
    
    print("\n1. **Fixture Congestion Integration**")
    print(f"   - {len(congested_teams)/len(cat_matches)*100:.1f}% of {cat_name} involved congested teams")
    print(f"   - Action: Reduce confidence by 10-15% when congestion_index >= 3")
    
    print("\n2. **H2H Data Quality**")
    print(f"   - {len(poor_h2h)/len(cat_matches)*100:.1f}% had limited H2H history")
    print(f"   - Action: Lower confidence when H2H matches < 3")
    
    print("\n3. **High Confidence Mistakes**")
    print(f"   - {len(high_conf_failures)} failures with >70% confidence")
    print(f"   - Action: Cap maximum confidence at 85% (not 100%)")
    
    print("\n4. **Odds Calibration**")
    print(f"   - {len(odds_surprises)} cases predicted favorites against bookmaker odds")
    print(f"   - Action: Add bookmaker odds check - if ML disagrees with odds by >20%, reduce confidence")
    
    # Draw-specific analysis
    draw_failures = len(failures['predicted_home_actual_draw']) + len(failures['predicted_away_actual_draw'])
    print(f"\n5. **Draw Prediction**")
    print(f"   - {draw_failures} failures missed draws")
    print(f"   - Action: Improve draw detection with balanced odds analysis (all odds 2.5-3.5)")
    
    # Model Comparison Analysis
    print(f"\n6. **Model Comparison (ML vs MC vs Poisson)**")
    mc_correct = sum(1 for m in cat_matches if m.get('mc_agreed', False))
    poisson_correct = sum(1 for m in cat_matches if m.get('poisson_agreed', False))
    
    print(f"   - When ML failed, MC was correct: {mc_correct}/{len(cat_matches)} ({mc_correct/len(cat_matches)*100:.1f}%)")
    print(f"   - When ML failed, Poisson was correct: {poisson_correct}/{len(cat_matches)} ({poisson_correct/len(cat_matches)*100:.1f}%)")
    
    # Cases where MC/Poisson disagreed with ML's overconfident prediction
    disagree_mc = [m for m in cat_matches if m.get('mc_prediction') != m['predicted'] and m['confidence'] > 0.7]
    disagree_poisson = [m for m in cat_matches if m.get('poisson_prediction') != m['predicted'] and m['confidence'] > 0.7]
    
    print(f"   - High-confidence ML failures where MC disagreed: {len(disagree_mc)}")
    print(f"   - High-confidence ML failures where Poisson disagreed: {len(disagree_poisson)}")
    print(f"   - **Action**: If MC disagrees with overconfident ML (>70%), use MC prediction")
    
    # BTTS Comparison
    if 'btts_failures' in failures:
        btts_fails = failures['btts_failures']
        mc_btts_correct = sum(1 for m in btts_fails if m.get('mc_btts_agreed', False))
        print(f"\n7. **BTTS Failures - MC Comparison**")
        print(f"   - When ML BTTS failed, MC was correct: {mc_btts_correct}/{len(btts_fails)} ({mc_btts_correct/len(btts_fails)*100:.1f}%)")
    
    # Over 2.5 Comparison
    if 'over25_failures' in failures:
        o25_fails = failures['over25_failures']
        mc_o25_correct = sum(1 for m in o25_fails if m.get('mc_over25_agreed', False))
        print(f"\n8. **Over 2.5 Failures - MC Comparison**")
        print(f"   - When ML O2.5 failed, MC was correct: {mc_o25_correct}/{len(o25_fails)} ({mc_o25_correct/len(o25_fails)*100:.1f}%)")
    
    # Generate detailed report
    report_path = REPORT_DIR / "ml_failure_analysis.md"
    
    # Collect only HDW failures for the main list
    all_hdw_failures = []
    for cat, matches in failures.items():
        if cat not in ['btts_failures', 'over25_failures']:
            all_hdw_failures.extend(matches)
            
    with open(report_path, 'w') as f:
        f.write("# ML Model Failure Analysis\n\n")
        f.write(f"**Period**: {weeks} weeks\n")
        f.write(f"**Total Matches**: {total_matches}\n")
        f.write(f"**HDW Failures**: {len(all_hdw_failures)} ({(len(all_hdw_failures)/total_matches)*100:.1f}%)\n\n")
        
        f.write("## Failure Categories\n\n")
        f.write("| Category | Count | % of Failures | Avg Confidence |\n")
        f.write("|----------|-------|---------------|----------------|\n")
        
        for category, matches in sorted(failures.items(), key=lambda x: len(x[1]), reverse=True):
            if matches:
                count = len(matches)
                avg_conf = np.mean([m['confidence'] for m in matches])
        f.write(f"### 3. Overconfidence\\n")
        f.write(f"1. **Congestion Penalty**: Reduce confidence by 10-15% when congestion >= 3\\n")
        f.write(f"2. **H2H Threshold**: Lower confidence by 5-10% when H2H < 3 matches\\n")
        f.write(f"3. **Confidence Cap**: Maximum 85% confidence (not 100%)\\n")
        f.write(f"4. **Odds Check**: If ML disagrees with bookmaker by >20%, reduce confidence\\n")
        f.write(f"5. **Draw Detection**: Better identify balanced matches (all odds 2.5-3.5)\\n")
        
        f.write(f"\n## Detailed List of Failures (HDW)\n\n")
        f.write("| Date | Home | Away | ML Pred | Actual | ML Conf | MC Pred | Poisson Pred | Odds (H/D/A) |\n")
        f.write("|------|------|------|---------|--------|---------|---------|--------------|--------------||\n")
        
        # Collect only HDW failures for the main list
        all_failures = []
        for cat, matches in failures.items():
            if cat not in ['btts_failures', 'over25_failures']:
                all_failures.extend(matches)
        
        # Sort by confidence (highest first)
        all_failures.sort(key=lambda x: x['confidence'], reverse=True)
            
        for m in all_failures:
            mc_symbol = "✓" if m.get('mc_agreed', False) else "✗"
            poisson_symbol = "✓" if m.get('poisson_agreed', False) else "✗"
            f.write(f"| {m['date']} | {m['home_team']} | {m['away_team']} | {m['predicted']} | {m['actual']} | {m['confidence']:.1%} | {m.get('mc_prediction', 'N/A')} {mc_symbol} | {m.get('poisson_prediction', 'N/A')} {poisson_symbol} | {m['odds_home']}/{m['odds_draw']}/{m['odds_away']} |\\n")

        # BTTS Failures
        f.write(f"\n## BTTS Failures\n\n")
        f.write("| Date | Match | ML Pred | Actual | ML Conf | MC Pred | Score |\n")
        f.write("|------|-------|---------|--------|---------|---------|-------|\\n")
        
        btts_fails = failures.get('btts_failures', [])
        btts_fails.sort(key=lambda x: x['confidence'], reverse=True)
        
        for m in btts_fails[:50]: # Top 50
             mc_symbol = "✓" if m.get('mc_btts_agreed', False) else "✗"
             f.write(f"| {m['date']} | {m['match']} | {m['predicted']} | {m['actual']} | {m['confidence']:.1%} | {m.get('mc_btts_prediction', 'N/A')} {mc_symbol} | {m['score']} |\n")
             
        # Over 2.5 Failures
        f.write(f"\n## Over 2.5 Goals Failures\n\n")
        f.write("| Date | Match | Pred | Actual | Conf | Score |\\n")
        f.write("|------|-------|------|--------|------|-------|\\n")
        
        o25_fails = failures.get('over25_failures', [])
        o25_fails.sort(key=lambda x: x['confidence'], reverse=True)
        
        for m in o25_fails[:50]: # Top 50
             f.write(f"| {m['date']} | {m['match']} | {m['predicted']} | {m['actual']} | {m['confidence']:.1%} | {m['score']} |\\n")

    
    print(f"\\n📄 Detailed report saved: {report_path}")
    print("\\n" + "=" * 70)
    print("✅ ANALYSIS COMPLETE")
    print("=" * 70)
    

if __name__ == "__main__":
    analyze_ml_failures(weeks=10)
