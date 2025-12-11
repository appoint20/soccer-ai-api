"""
Analyze prediction failures by league
Focuses on Championship, League One, League Two
"""
import pandas as pd
from pathlib import Path
from datetime import datetime, timedelta
from collections import defaultdict
import sys

sys.path.insert(0, str(Path(__file__).parent.parent))

from app.core.ml_predictor import get_ml_predictor
from app.core.poisson import PoissonPredictor
from app.core.monte_carlo import MonteCarloSimulator
from app.core.derby_detector import get_derby_detector


def load_and_analyze_failures(weeks=15):
    """Load matches and analyze failures by league"""
    
    # Load data from separate worksheets
    data_dir = Path(__file__).parent.parent / "data" / "historical"
    excel_file = list(data_dir.glob("*2025-2026.xlsx"))[0]
    
    # Read each league from its worksheet
    sheets = {
        'Championship': 'E1',
        'League One': 'E2',
        'League Two': 'E3'
    }
    
    # Filter by date
    end_date = datetime.now()
    start_date = end_date - timedelta(weeks=weeks)
    
    # Initialize ML predictor
    ml_predictor = get_ml_predictor()
    derby_detector = get_derby_detector()
    
    # Analyze failures
    failures_by_league = defaultdict(list)
    
    for league_name, sheet_name in sheets.items():
        try:
            df = pd.read_excel(excel_file, sheet_name=sheet_name)
            df = df[(df['Date'] >= start_date) & (df['Date'] <= end_date)]
            
            print(f"Processing {league_name}: {len(df)} matches")
            
            for _, row in df.iterrows():
                # Get actual result
                ftr = row.get('FTR', '')
                if not ftr:
                    continue
                
                # Get odds
                home_odds = row.get('B365H', row.get('PSH', 2.0)) or 2.0
                draw_odds = row.get('B365D', row.get('PSD', 3.3)) or 3.3
                away_odds = row.get('B365A', row.get('PSA', 3.5)) or 3.5
                
                # Determine ML prediction (simplified - based on odds)
                if home_odds < draw_odds and home_odds < away_odds:
                    ml_pred = 'H'
                    pred_odds = home_odds
                elif away_odds < draw_odds and away_odds < home_odds:
                    ml_pred = 'A'
                    pred_odds = away_odds
                else:
                    ml_pred = 'D'
                    pred_odds = draw_odds
                
                # Check if prediction failed
                if ml_pred != ftr:
                    # Analyze failure
                    home_team = row.get('HomeTeam', '')
                    away_team = row.get('AwayTeam', '')
                    
                    # Check if derby
                    is_derby, derby_name = derby_detector.is_derby(home_team, away_team)
                    
                    # Categorize failure type
                    failure_type = ""
                    if ml_pred == 'H' and ftr == 'D':
                        failure_type = "Favorite Failed to Win (Draw)"
                    elif ml_pred == 'H' and ftr == 'A':
                        failure_type = "Home Favorite Lost (Away Win)"
                    elif ml_pred == 'A' and ftr == 'H':
                        failure_type = "Away Favorite Lost (Home Win)"
                    elif ml_pred == 'A' and ftr == 'D':
                        failure_type = "Away Favorite Drew"
                    elif ml_pred == 'D' and ftr in ['H', 'A']:
                        failure_type = "Draw Prediction Wrong (Result Decisive)"
                    else:
                        failure_type = "Other"
                    
                    failures_by_league[league_name].append({
                        'home': home_team,
                        'away': away_team,
                        'predicted': ml_pred,
                        'actual': ftr,
                        'pred_odds': pred_odds,
                        'home_odds': home_odds,
                        'draw_odds': draw_odds,
                        'away_odds': away_odds,
                        'failure_type': failure_type,
                        'is_derby': is_derby,
                        'derby_name': derby_name,
                        'date': row['Date'],
                        'score': f"{row.get('FTHG', 0)}-{row.get('FTAG', 0)}"
                    })
        except Exception as e:
            print(f"Error processing {league_name}: {e}")
            continue
    
    return failures_by_league


def generate_failure_report(failures_by_league):
    """Generate detailed failure analysis report"""
    
    report = []
    report.append("# Lower League Prediction Failure Analysis\n")
    report.append(f"**Generated**: {datetime.now().strftime('%Y-%m-%d %H:%M')}\n\n")
    
    for league, failures in sorted(failures_by_league.items()):
        report.append(f"## {league}\n\n")
        report.append(f"**Total Failures**: {len(failures)}\n\n")
        
        # Categorize by failure type
        failure_types = defaultdict(int)
        for f in failures:
            failure_types[f['failure_type']] += 1
        
        report.append("### Failure Types Breakdown\n\n")
        for ftype, count in sorted(failure_types.items(), key=lambda x: x[1], reverse=True):
            pct = (count / len(failures)) * 100
            report.append(f"- **{ftype}**: {count} ({pct:.1f}%)\n")
        report.append("\n")
        
        # Odds analysis
        report.append("### Odds Analysis of Failed Predictions\n\n")
        low_odds_failures = [f for f in failures if f['pred_odds'] < 1.5]
        medium_odds_failures = [f for f in failures if 1.5 <= f['pred_odds'] < 2.5]
        high_odds_failures = [f for f in failures if f['pred_odds'] >= 2.5]
        
        report.append(f"- **Low Odds (<1.5)**: {len(low_odds_failures)} failures - Strong favorites that lost\n")
        report.append(f"- **Medium Odds (1.5-2.5)**: {len(medium_odds_failures)} failures - Moderate favorites\n")
        report.append(f"- **High Odds (>2.5)**: {len(high_odds_failures)} failures - Underdogs/Draws predicted\n\n")
        
        # Derby analysis
        derby_failures = [f for f in failures if f['is_derby']]
        if derby_failures:
            report.append(f"### Derby Matches: {len(derby_failures)} failures\n\n")
            for df in derby_failures:
                report.append(f"- {df['home']} vs {df['away']} ({df['derby_name']}): Predicted {df['predicted']}, Result {df['actual']} ({df['score']})\n")
            report.append("\n")
        
        # Top 10 worst failures (lowest odds that failed)
        report.append("### Top 10 Worst Failures (Strongest Favorites That Lost)\n\n")
        worst_failures = sorted(failures, key=lambda x: x['pred_odds'])[:10]
        
        report.append("| Match | Predicted | Actual | Odds | Score | Type |\n")
        report.append("|-------|-----------|--------|------|-------|------|\n")
        for wf in worst_failures:
            report.append(f"| {wf['home']} vs {wf['away']} | {wf['predicted']} | {wf['actual']} | {wf['pred_odds']:.2f} | {wf['score']} | {wf['failure_type']} |\n")
        report.append("\n")
        
        # Sample failures with full odds
        report.append("### Sample Failures with Full Odds Context\n\n")
        sample_failures = failures[:5]
        for sf in sample_failures:
            report.append(f"**{sf['home']} vs {sf['away']}** ({sf['date'].strftime('%Y-%m-%d')})\n")
            report.append(f"- Predicted: {sf['predicted']} (Odds: {sf['pred_odds']:.2f})\n")
            report.append(f"- Actual: {sf['actual']} ({sf['score']})\n")
            report.append(f"- Full Odds: H={sf['home_odds']:.2f}, D={sf['draw_odds']:.2f}, A={sf['away_odds']:.2f}\n")
            report.append(f"- Failure Type: {sf['failure_type']}\n\n")
        
        report.append("---\n\n")
    
    # Overall summary
    report.append("## Key Findings\n\n")
    
    total_failures = sum(len(f) for f in failures_by_league.values())
    report.append(f"**Total Failures Analyzed**: {total_failures}\n\n")
    
    # Common patterns
    all_failures = []
    for failures in failures_by_league.values():
        all_failures.extend(failures)
    
    all_failure_types = defaultdict(int)
    for f in all_failures:
        all_failure_types[f['failure_type']] += 1
    
    report.append("### Most Common Failure Patterns:\n\n")
    for i, (ftype, count) in enumerate(sorted(all_failure_types.items(), key=lambda x: x[1], reverse=True)[:5], 1):
        pct = (count / total_failures) * 100
        report.append(f"{i}. **{ftype}**: {count} ({pct:.1f}%)\n")
    
    report.append("\n### Recommendations:\n\n")
    
    # Analyze odds distribution
    low_odds_total = sum(1 for f in all_failures if f['pred_odds'] < 1.5)
    if low_odds_total > total_failures * 0.3:
        report.append("- ⚠️ **Too many strong favorite failures** - Consider avoiding bets with odds < 1.5\n")
    
    favorite_draw_failures = sum(1 for f in all_failures if "Draw" in f['failure_type'])
    if favorite_draw_failures > total_failures * 0.4:
        report.append("- ⚠️ **Draw miss-prediction is major issue** - Improve draw detection logic\n")
    
    derby_total = sum(1 for f in all_failures if f['is_derby'])
    if derby_total > 0:
        report.append(f"- ⚠️ **Derby matches causing issues** - {derby_total} derby failures detected\n")
    
    return ''.join(report)


if __name__ == "__main__":
    print("Analyzing prediction failures for Championship, League One, League Two...")
    failures = load_and_analyze_failures(weeks=15)
    
    report = generate_failure_report(failures)
    
    # Save report
    output_path = Path.home() / ".gemini" / "antigravity" / "brain" / "3d63d768-8366-4689-9f50-05ba8a1be4a6" / "LOWER_LEAGUE_FAILURE_ANALYSIS.md"
    with open(output_path, 'w') as f:
        f.write(report)
    
    print(f"✅ Analysis complete. Report saved to: {output_path}")
    print(f"\nSummary:")
    for league, failures_list in failures.items():
        print(f"  {league}: {len(failures_list)} failures")
