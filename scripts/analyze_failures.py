#!/usr/bin/env python3
"""
Failure Analysis — WHY did Over 2.5 and BTTS predictions fail?
Examines Poisson, Monte Carlo, ML (back-calculated), and match stats for each wrong prediction.
Categorizes failures by root cause.
"""

import json
import urllib.request
from datetime import datetime, timedelta
from collections import defaultdict

API_URL = "http://localhost:5165/api/analysis"
WEEKS = 10

def fetch_predictions(date):
    try:
        url = f"{API_URL}?date={date.isoformat()}"
        with urllib.request.urlopen(url, timeout=30) as response:
            if response.status == 200:
                data = json.loads(response.read().decode('utf-8'))
                return data.get('matches', [])
    except Exception as e:
        print(f"  Error for {date}: {e}")
    return []

def back_calc_ml(combined, poisson, mc):
    """Back-calculate ML probability: combined = 0.40*poisson + 0.35*ml + 0.25*mc"""
    if combined is None or poisson is None or mc is None:
        return None
    ml = (combined - 0.40 * poisson - 0.25 * mc) / 0.35
    return max(0, min(1, ml))  # clamp

def analyze():
    today = datetime.now().date()
    all_matches = []

    print(f"Fetching {WEEKS} weeks of data...")
    for week in range(WEEKS):
        for day_offset in range(7):
            date = today - timedelta(weeks=week, days=day_offset)
            matches = fetch_predictions(date)
            for m in matches:
                result = m.get('result')
                if not result or 'actual_score' not in result:
                    continue
                score = result['actual_score']
                parts = score.split(':')
                if len(parts) != 2:
                    continue
                home_goals, away_goals = int(parts[0]), int(parts[1])
                total_goals = home_goals + away_goals

                pred = m.get('prediction', {})
                models_data = m.get('models', {})
                poisson = models_data.get('poisson', {})
                mc = models_data.get('monte_carlo', {}) or models_data.get('monteCarlo', {})

                # Over 2.5
                p_over25 = pred.get('over25', {})
                combined_over25 = p_over25.get('probability', 0)
                predicted_over25 = p_over25.get('prediction', False)
                poisson_over25 = poisson.get('over25', None) or poisson.get('Over25', None)
                mc_over25 = mc.get('over25', None) or mc.get('Over25', None)
                ml_over25 = back_calc_ml(combined_over25, poisson_over25, mc_over25) if poisson_over25 and mc_over25 else None
                actual_over25 = total_goals > 2

                # BTTS
                p_btts = pred.get('btts', {})
                combined_btts = p_btts.get('probability', 0)
                predicted_btts = p_btts.get('prediction', False)
                poisson_btts = poisson.get('btts', None) or poisson.get('BTTS', None)
                mc_btts = mc.get('btts', None) or mc.get('BTTS', None)
                ml_btts = back_calc_ml(combined_btts, poisson_btts, mc_btts) if poisson_btts and mc_btts else None
                actual_btts = home_goals > 0 and away_goals > 0

                # Team stats
                h_stats = m.get('home_stats', {}) or m.get('homeStats', {})
                a_stats = m.get('away_stats', {}) or m.get('awayStats', {})

                all_matches.append({
                    'date': m.get('date', ''),
                    'league': m.get('league', ''),
                    'home': m.get('home_team', '') or m.get('homeTeam', ''),
                    'away': m.get('away_team', '') or m.get('awayTeam', ''),
                    'score': score,
                    'total_goals': total_goals,
                    # Over 2.5
                    'predicted_over25': predicted_over25,
                    'actual_over25': actual_over25,
                    'correct_over25': predicted_over25 == actual_over25,
                    'combined_over25': combined_over25,
                    'poisson_over25': poisson_over25,
                    'mc_over25': mc_over25,
                    'ml_over25': ml_over25,
                    # BTTS
                    'predicted_btts': predicted_btts,
                    'actual_btts': actual_btts,
                    'correct_btts': predicted_btts == actual_btts,
                    'combined_btts': combined_btts,
                    'poisson_btts': poisson_btts,
                    'mc_btts': mc_btts,
                    'ml_btts': ml_btts,
                    # Team stats
                    'home_goals_scored_avg': h_stats.get('goals_scored_avg', 0) or h_stats.get('goalsScoredAvg', 0),
                    'home_goals_conceded_avg': h_stats.get('goals_conceded_avg', 0) or h_stats.get('goalsConcededAvg', 0),
                    'away_goals_scored_avg': a_stats.get('goals_scored_avg', 0) or a_stats.get('goalsScoredAvg', 0),
                    'away_goals_conceded_avg': a_stats.get('goals_conceded_avg', 0) or a_stats.get('goalsConcededAvg', 0),
                    'home_over25_rate': h_stats.get('over25_rate', 0) or h_stats.get('over25Rate', 0),
                    'away_over25_rate': a_stats.get('over25_rate', 0) or a_stats.get('over25Rate', 0),
                    'home_btts_rate': h_stats.get('btts_rate', 0) or h_stats.get('bttsRate', 0),
                    'away_btts_rate': a_stats.get('btts_rate', 0) or a_stats.get('bttsRate', 0),
                })

    total = len(all_matches)
    if total == 0:
        print("No matches found!")
        return

    print(f"\nTotal matches analyzed: {total}")

    # ═══════════════════════════════════════════════
    # OVER 2.5 FAILURE ANALYSIS
    # ═══════════════════════════════════════════════
    over25_wrong = [m for m in all_matches if not m['correct_over25']]
    over25_right = [m for m in all_matches if m['correct_over25']]

    print(f"\n{'═'*70}")
    print(f"OVER 2.5 FAILURE ANALYSIS — {len(over25_wrong)} wrong out of {total}")
    print(f"{'═'*70}")

    # Split by prediction direction
    predicted_over_but_under = [m for m in over25_wrong if m['predicted_over25'] and not m['actual_over25']]
    predicted_under_but_over = [m for m in over25_wrong if not m['predicted_over25'] and m['actual_over25']]

    print(f"\n  Predicted OVER 2.5 but actual UNDER: {len(predicted_over_but_under)}")
    print(f"  Predicted UNDER 2.5 but actual OVER:  {len(predicted_under_but_over)}")

    # Model agreement analysis for predicted_over_but_under
    if predicted_over_but_under:
        print(f"\n  --- Predicted OVER 2.5 but was UNDER ({len(predicted_over_but_under)} matches) ---")
        poisson_also_wrong = sum(1 for m in predicted_over_but_under if m['poisson_over25'] and m['poisson_over25'] > 0.5)
        mc_also_wrong = sum(1 for m in predicted_over_but_under if m['mc_over25'] and m['mc_over25'] > 0.5)
        ml_also_wrong = sum(1 for m in predicted_over_but_under if m['ml_over25'] and m['ml_over25'] > 0.5)
        
        print(f"    Poisson also said Over 2.5 (>50%): {poisson_also_wrong}/{len(predicted_over_but_under)} ({poisson_also_wrong/len(predicted_over_but_under)*100:.0f}%)")
        print(f"    MC also said Over 2.5 (>50%):      {mc_also_wrong}/{len(predicted_over_but_under)} ({mc_also_wrong/len(predicted_over_but_under)*100:.0f}%)")
        print(f"    ML also said Over 2.5 (>50%):      {ml_also_wrong}/{len(predicted_over_but_under)} ({ml_also_wrong/len(predicted_over_but_under)*100:.0f}%)")
        
        all_three_wrong = sum(1 for m in predicted_over_but_under 
                             if m['poisson_over25'] and m['poisson_over25'] > 0.5
                             and m['mc_over25'] and m['mc_over25'] > 0.5 
                             and m['ml_over25'] and m['ml_over25'] > 0.5)
        print(f"    ALL 3 models wrong together:       {all_three_wrong}/{len(predicted_over_but_under)} ({all_three_wrong/len(predicted_over_but_under)*100:.0f}%)")

        # Average probabilities when wrong
        avg_poisson = sum(m['poisson_over25'] for m in predicted_over_but_under if m['poisson_over25']) / max(1, sum(1 for m in predicted_over_but_under if m['poisson_over25']))
        avg_mc = sum(m['mc_over25'] for m in predicted_over_but_under if m['mc_over25']) / max(1, sum(1 for m in predicted_over_but_under if m['mc_over25']))
        avg_ml = sum(m['ml_over25'] for m in predicted_over_but_under if m['ml_over25']) / max(1, sum(1 for m in predicted_over_but_under if m['ml_over25']))
        avg_combined = sum(m['combined_over25'] for m in predicted_over_but_under) / len(predicted_over_but_under)
        
        print(f"\n    Avg probabilities when wrong:")
        print(f"      Combined: {avg_combined:.1%}")
        print(f"      Poisson:  {avg_poisson:.1%}")
        print(f"      MC:       {avg_mc:.1%}")
        print(f"      ML:       {avg_ml:.1%}")

        # Score distribution
        score_dist = defaultdict(int)
        for m in predicted_over_but_under:
            score_dist[m['score']] += 1
        print(f"\n    Score distribution (predicted Over but was Under):")
        for score, count in sorted(score_dist.items(), key=lambda x: -x[1])[:10]:
            print(f"      {score}: {count} matches")

        # Team stats when wrong  
        avg_home_rate = sum(m['home_over25_rate'] for m in predicted_over_but_under) / len(predicted_over_but_under)
        avg_away_rate = sum(m['away_over25_rate'] for m in predicted_over_but_under) / len(predicted_over_but_under)
        print(f"\n    Avg team Over 2.5 rates when wrong:")
        print(f"      Home team avg Over 2.5 rate: {avg_home_rate:.1%}")
        print(f"      Away team avg Over 2.5 rate: {avg_away_rate:.1%}")

    # Same for predicted_under_but_over
    if predicted_under_but_over:
        print(f"\n  --- Predicted UNDER 2.5 but was OVER ({len(predicted_under_but_over)} matches) ---")
        poisson_also_wrong = sum(1 for m in predicted_under_but_over if m['poisson_over25'] and m['poisson_over25'] <= 0.5)
        mc_also_wrong = sum(1 for m in predicted_under_but_over if m['mc_over25'] and m['mc_over25'] <= 0.5)
        ml_also_wrong = sum(1 for m in predicted_under_but_over if m['ml_over25'] and m['ml_over25'] <= 0.5)
        
        print(f"    Poisson also said Under 2.5 (<=50%): {poisson_also_wrong}/{len(predicted_under_but_over)} ({poisson_also_wrong/len(predicted_under_but_over)*100:.0f}%)")
        print(f"    MC also said Under 2.5 (<=50%):      {mc_also_wrong}/{len(predicted_under_but_over)} ({mc_also_wrong/len(predicted_under_but_over)*100:.0f}%)")
        print(f"    ML also said Under 2.5 (<=50%):      {ml_also_wrong}/{len(predicted_under_but_over)} ({ml_also_wrong/len(predicted_under_but_over)*100:.0f}%)")

        avg_poisson = sum(m['poisson_over25'] for m in predicted_under_but_over if m['poisson_over25']) / max(1, sum(1 for m in predicted_under_but_over if m['poisson_over25']))
        avg_mc = sum(m['mc_over25'] for m in predicted_under_but_over if m['mc_over25']) / max(1, sum(1 for m in predicted_under_but_over if m['mc_over25']))
        avg_ml = sum(m['ml_over25'] for m in predicted_under_but_over if m['ml_over25']) / max(1, sum(1 for m in predicted_under_but_over if m['ml_over25']))
        avg_combined = sum(m['combined_over25'] for m in predicted_under_but_over) / len(predicted_under_but_over)
        
        print(f"\n    Avg probabilities when wrong:")
        print(f"      Combined: {avg_combined:.1%}")
        print(f"      Poisson:  {avg_poisson:.1%}")
        print(f"      MC:       {avg_mc:.1%}")
        print(f"      ML:       {avg_ml:.1%}")

        score_dist = defaultdict(int)
        for m in predicted_under_but_over:
            score_dist[m['score']] += 1
        print(f"\n    Score distribution (predicted Under but was Over):")
        for score, count in sorted(score_dist.items(), key=lambda x: -x[1])[:10]:
            print(f"      {score}: {count} matches")

    # ═══════════════════════════════════════════════
    # BTTS FAILURE ANALYSIS
    # ═══════════════════════════════════════════════
    btts_wrong = [m for m in all_matches if not m['correct_btts']]
    
    print(f"\n{'═'*70}")
    print(f"BTTS FAILURE ANALYSIS — {len(btts_wrong)} wrong out of {total}")
    print(f"{'═'*70}")

    predicted_btts_but_no = [m for m in btts_wrong if m['predicted_btts'] and not m['actual_btts']]
    predicted_no_but_btts = [m for m in btts_wrong if not m['predicted_btts'] and m['actual_btts']]

    print(f"\n  Predicted BTTS Yes but actual No: {len(predicted_btts_but_no)}")
    print(f"  Predicted BTTS No but actual Yes: {len(predicted_no_but_btts)}")

    if predicted_btts_but_no:
        print(f"\n  --- Predicted BTTS Yes but was No ({len(predicted_btts_but_no)} matches) ---")
        poisson_also_wrong = sum(1 for m in predicted_btts_but_no if m['poisson_btts'] and m['poisson_btts'] > 0.5)
        mc_also_wrong = sum(1 for m in predicted_btts_but_no if m['mc_btts'] and m['mc_btts'] > 0.5)
        ml_also_wrong = sum(1 for m in predicted_btts_but_no if m['ml_btts'] and m['ml_btts'] > 0.5)
        
        print(f"    Poisson also said BTTS (>50%): {poisson_also_wrong}/{len(predicted_btts_but_no)} ({poisson_also_wrong/len(predicted_btts_but_no)*100:.0f}%)")
        print(f"    MC also said BTTS (>50%):      {mc_also_wrong}/{len(predicted_btts_but_no)} ({mc_also_wrong/len(predicted_btts_but_no)*100:.0f}%)")
        print(f"    ML also said BTTS (>50%):      {ml_also_wrong}/{len(predicted_btts_but_no)} ({ml_also_wrong/len(predicted_btts_but_no)*100:.0f}%)")

        all_three_wrong = sum(1 for m in predicted_btts_but_no 
                             if m['poisson_btts'] and m['poisson_btts'] > 0.5
                             and m['mc_btts'] and m['mc_btts'] > 0.5 
                             and m['ml_btts'] and m['ml_btts'] > 0.5)
        print(f"    ALL 3 models wrong together:   {all_three_wrong}/{len(predicted_btts_but_no)} ({all_three_wrong/len(predicted_btts_but_no)*100:.0f}%)")

        avg_poisson = sum(m['poisson_btts'] for m in predicted_btts_but_no if m['poisson_btts']) / max(1, sum(1 for m in predicted_btts_but_no if m['poisson_btts']))
        avg_mc = sum(m['mc_btts'] for m in predicted_btts_but_no if m['mc_btts']) / max(1, sum(1 for m in predicted_btts_but_no if m['mc_btts']))
        avg_ml = sum(m['ml_btts'] for m in predicted_btts_but_no if m['ml_btts']) / max(1, sum(1 for m in predicted_btts_but_no if m['ml_btts']))
        avg_combined = sum(m['combined_btts'] for m in predicted_btts_but_no) / len(predicted_btts_but_no)
        
        print(f"\n    Avg probabilities when wrong:")
        print(f"      Combined: {avg_combined:.1%}")
        print(f"      Poisson:  {avg_poisson:.1%}")
        print(f"      MC:       {avg_mc:.1%}")
        print(f"      ML:       {avg_ml:.1%}")

        score_dist = defaultdict(int)
        for m in predicted_btts_but_no:
            score_dist[m['score']] += 1
        print(f"\n    Score distribution (predicted BTTS but wasn't):")
        for score, count in sorted(score_dist.items(), key=lambda x: -x[1])[:10]:
            print(f"      {score}: {count} matches")

        # Which team failed to score?
        home_clean = sum(1 for m in predicted_btts_but_no if int(m['score'].split(':')[1]) == 0)
        away_clean = sum(1 for m in predicted_btts_but_no if int(m['score'].split(':')[0]) == 0)
        both_0 = sum(1 for m in predicted_btts_but_no if m['score'] == '0:0')
        print(f"\n    Who didn't score?")
        print(f"      Away team kept clean sheet (home scored 0): {away_clean}")
        print(f"      Home team kept clean sheet (away scored 0): {home_clean}")
        print(f"      Both 0-0: {both_0}")

    if predicted_no_but_btts:
        print(f"\n  --- Predicted BTTS No but was Yes ({len(predicted_no_but_btts)} matches) ---")
        poisson_also_wrong = sum(1 for m in predicted_no_but_btts if m['poisson_btts'] and m['poisson_btts'] <= 0.5)
        mc_also_wrong = sum(1 for m in predicted_no_but_btts if m['mc_btts'] and m['mc_btts'] <= 0.5)
        ml_also_wrong = sum(1 for m in predicted_no_but_btts if m['ml_btts'] and m['ml_btts'] <= 0.5)
        
        print(f"    Poisson also said No BTTS (<=50%): {poisson_also_wrong}/{len(predicted_no_but_btts)} ({poisson_also_wrong/len(predicted_no_but_btts)*100:.0f}%)")
        print(f"    MC also said No BTTS (<=50%):      {mc_also_wrong}/{len(predicted_no_but_btts)} ({mc_also_wrong/len(predicted_no_but_btts)*100:.0f}%)")
        print(f"    ML also said No BTTS (<=50%):      {ml_also_wrong}/{len(predicted_no_but_btts)} ({ml_also_wrong/len(predicted_no_but_btts)*100:.0f}%)")

        avg_combined = sum(m['combined_btts'] for m in predicted_no_but_btts) / len(predicted_no_but_btts)
        print(f"\n    Avg combined probability: {avg_combined:.1%}")

    # ═══════════════════════════════════════════════
    # PROBABILITY CALIBRATION — are the probabilities reliable?
    # ═══════════════════════════════════════════════
    print(f"\n{'═'*70}")
    print(f"PROBABILITY CALIBRATION CHECK")
    print(f"{'═'*70}")
    
    # Bin by combined probability and check actual rates
    for market, pred_key, actual_key in [("Over 2.5", "combined_over25", "actual_over25"), ("BTTS", "combined_btts", "actual_btts")]:
        print(f"\n  {market} — Combined Probability vs Actual Rate:")
        bins = [(0.40, 0.50), (0.50, 0.55), (0.55, 0.60), (0.60, 0.65), (0.65, 0.70), (0.70, 0.80), (0.80, 1.0)]
        for low, high in bins:
            in_bin = [m for m in all_matches if low <= m[pred_key] < high]
            if len(in_bin) < 5:
                continue
            actual_rate = sum(1 for m in in_bin if m[actual_key]) / len(in_bin)
            print(f"    Prob {low:.0%}-{high:.0%}: {len(in_bin):>4} matches, actual rate: {actual_rate:.1%} {'✅' if abs(actual_rate - (low+high)/2) < 0.10 else '⚠️  MISCALIBRATED'}")

    # ═══════════════════════════════════════════════
    # INDIVIDUAL MODEL ACCURACY
    # ═══════════════════════════════════════════════
    print(f"\n{'═'*70}")
    print(f"INDIVIDUAL MODEL ACCURACY (who is best?)")
    print(f"{'═'*70}")
    
    has_all_data = [m for m in all_matches if m['poisson_over25'] and m['mc_over25'] and m['ml_over25']]
    if has_all_data:
        for market, poisson_key, mc_key, ml_key, actual_key in [
            ("Over 2.5", "poisson_over25", "mc_over25", "ml_over25", "actual_over25"),
            ("BTTS",     "poisson_btts",   "mc_btts",   "ml_btts",   "actual_btts")
        ]:
            relevant = [m for m in has_all_data if m[poisson_key] is not None and m[mc_key] is not None and m[ml_key] is not None]
            if not relevant:
                continue
            poisson_correct = sum(1 for m in relevant if (m[poisson_key] > 0.5) == m[actual_key])
            mc_correct = sum(1 for m in relevant if (m[mc_key] > 0.5) == m[actual_key])
            ml_correct = sum(1 for m in relevant if (m[ml_key] > 0.5) == m[actual_key])
            combined_correct = sum(1 for m in relevant if (m[f'combined_{market.lower().replace(" ", "").replace(".", "")}'] > 0.5) == m[actual_key])
            n = len(relevant)
            
            # For combined key, handle naming
            comb_key = f"combined_{'over25' if 'Over' in market else 'btts'}"
            combined_correct = sum(1 for m in relevant if (m[comb_key] > 0.5) == m[actual_key])
            
            print(f"\n  {market} ({n} matches):")
            print(f"    Poisson:  {poisson_correct}/{n} ({poisson_correct/n*100:.1f}%)")
            print(f"    MC:       {mc_correct}/{n} ({mc_correct/n*100:.1f}%)")
            print(f"    ML:       {ml_correct}/{n} ({ml_correct/n*100:.1f}%)")
            print(f"    Combined: {combined_correct}/{n} ({combined_correct/n*100:.1f}%)")

    # ═══════════════════════════════════════════════
    # LEAGUE-LEVEL FAILURE PATTERNS
    # ═══════════════════════════════════════════════
    print(f"\n{'═'*70}")
    print(f"WORST LEAGUES (highest failure rate)")
    print(f"{'═'*70}")
    
    league_stats = defaultdict(lambda: {'total': 0, 'over25_wrong': 0, 'btts_wrong': 0})
    for m in all_matches:
        lg = m['league']
        league_stats[lg]['total'] += 1
        if not m['correct_over25']:
            league_stats[lg]['over25_wrong'] += 1
        if not m['correct_btts']:
            league_stats[lg]['btts_wrong'] += 1

    print(f"\n  {'League':<16} {'Total':>5}  {'O2.5 Wrong':>10} {'O2.5 Fail%':>10}  {'BTTS Wrong':>10} {'BTTS Fail%':>10}")
    for lg, s in sorted(league_stats.items(), key=lambda x: -x[1]['over25_wrong']):
        if s['total'] < 10:
            continue
        o_pct = s['over25_wrong'] / s['total'] * 100
        b_pct = s['btts_wrong'] / s['total'] * 100
        print(f"  {lg:<16} {s['total']:>5}  {s['over25_wrong']:>10} {o_pct:>9.1f}%  {s['btts_wrong']:>10} {b_pct:>9.1f}%")

if __name__ == "__main__":
    analyze()
