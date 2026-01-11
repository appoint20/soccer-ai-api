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
    ml_predictor = get_ml_predictor()
    
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
        
        # ML prediction
        ml_result = ml_predictor.predict(home_stats, away_stats, odds, h2h_features)
        predicted = ml_result['prediction']
        confidence = ml_result['confidence']
        
        # Get congestion
        home_congestion = calculate_fixture_congestion(home_team, league_id, match_date, all_historical_df)
        away_congestion = calculate_fixture_congestion(away_team, league_id, match_date, all_historical_df)
        
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
                'home_team': home_team,
                'away_team': away_team,
                'predicted': predicted,
                'actual': actual_result,
                'confidence': confidence,
                'odds_home': odds['home'],
                'odds_draw': odds['draw'],
                'odds_away': odds['away'],
                'home_form': home_stats.get('form', []),
                'away_form': away_stats.get('form', []),
                'home_congestion': home_congestion['congestion_index'],
                'away_congestion': away_congestion['congestion_index'],
                'h2h_matches': h2h_features.get('matches', 0)
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
    
    # Generate detailed report
    report_path = REPORT_DIR / "ml_failure_analysis.md"
    
    with open(report_path, 'w') as f:
        f.write("# ML Model Failure Analysis\\n\\n")
        f.write(f"**Period**: {weeks} weeks\\n")
        f.write(f"**Total Matches**: {total_matches}\\n")
        f.write(f"**Failures**: {total_failures} ({total_failures/total_matches*100:.1f}%)\\n\\n")
        
        f.write("## Failure Categories\\n\\n")
        f.write("| Category | Count | % of Failures | Avg Confidence |\\n")
        f.write("|----------|-------|---------------|----------------|\\n")
        
        for category, matches in sorted(failures.items(), key=lambda x: len(x[1]), reverse=True):
            if matches:
                count = len(matches)
                pct = count / total_failures * 100
                avg_conf = np.mean([m['confidence'] for m in matches])
                f.write(f"| {category.replace('_', ' ').title()} | {count} | {pct:.1f}% | {avg_conf:.1%} |\\n")
        
        f.write(f"\\n## Key Findings\\n\\n")
        f.write(f"### 1. Fixture Congestion Impact\\n")
        f.write(f"- **{len(congested_teams)}** failures ({len(congested_teams)/total_failures*100:.1f}%) involved teams with congestion_index >= 3\\n")
        f.write(f"- Teams playing 2+ matches in 7 days are more unpredictable\\n\\n")
        
        f.write(f"### 2. H2H Data Quality\\n")
        f.write(f"- **{len(poor_h2h)}** failures ({len(poor_h2h)/total_failures*100:.1f}%) had < 3 H2H matches\\n")
        f.write(f"- Predictions less reliable without H2H context\\n\\n")
        
        f.write(f"### 3. Overconfidence\\n")
        f.write(f"- **{len(high_conf_failures)}** high-confidence failures (>70%)\\n")
        f.write(f"- Model sometimes too confident in uncertain matchups\\n\\n")
        
        f.write(f"## 🎯 Actionable Improvements\\n\\n")
        f.write(f"1. **Congestion Penalty**: Reduce confidence by 10-15% when congestion >= 3\\n")
        f.write(f"2. **H2H Threshold**: Lower confidence by 5-10% when H2H < 3 matches\\n")
        f.write(f"3. **Confidence Cap**: Maximum 85% confidence (not 100%)\\n")
        f.write(f"4. **Odds Check**: If ML disagrees with bookmaker by >20%, reduce confidence\\n")
        f.write(f"5. **Draw Detection**: Better identify balanced matches (all odds 2.5-3.5)\\n")
    
    print(f"\\n📄 Detailed report saved: {report_path}")
    print("\\n" + "=" * 70)
    print("✅ ANALYSIS COMPLETE")
    print("=" * 70)
    

if __name__ == "__main__":
    analyze_ml_failures(weeks=10)
