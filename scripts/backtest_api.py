"""
Backtest API script.
Queries the Analysis API for the last 10 weeks and calculates accuracy stats based on Final Predictions.
"""

import urllib.request
import json
import datetime
import pandas as pd
from tabulate import tabulate
import time
import os

# Configuration
API_URL = "http://localhost:5165/api/analysis" 

# --- EXCEL FALLBACK LOGIC ---
EXCEL_DIR = "src/soccer-gpt-infrastructure/Data/historical"
FALLBACK_DF = None

def load_excel_fallback():
    global FALLBACK_DF
    try:
        files = [f for f in os.listdir(EXCEL_DIR) if f.endswith('.xlsx')]
        dfs = []
        for file in files:
            path = os.path.join(EXCEL_DIR, file)
            df = pd.read_excel(path, engine='openpyxl')
            dfs.append(df)
        
        if dfs:
            FALLBACK_DF = pd.concat(dfs, ignore_index=True)
            FALLBACK_DF['DateStr'] = pd.to_datetime(FALLBACK_DF['Date'], dayfirst=True, errors='coerce').dt.strftime('%Y-%m-%d')
            print(f"✅ Loaded historical Excel fallback ({len(FALLBACK_DF)} matches) to recover missing API scores.")
    except Exception as e:
        print(f"⚠️ Excel fallback disabled: {e}")

def lookup_excel_result(date_str, home_team, away_team):
    if FALLBACK_DF is None or FALLBACK_DF.empty or not date_str:
        return None
        
    date_part = date_str.split('T')[0]
    
    # Fuzzy match on the primary first 5 characters due to naming inconsistencies
    h_prefix = home_team[:5].lower()
    a_prefix = away_team[:5].lower()
    
    matches = FALLBACK_DF[
        (FALLBACK_DF['DateStr'] == date_part) & 
        (FALLBACK_DF['HomeTeam'].str.lower().str.contains(h_prefix, na=False)) &
        (FALLBACK_DF['AwayTeam'].str.lower().str.contains(a_prefix, na=False))
    ]
    
    if not matches.empty:
        row = matches.iloc[0]
        if pd.notna(row.get('FTHG')) and pd.notna(row.get('FTAG')):
            return f"{int(row['FTHG'])}:{int(row['FTAG'])}"
    return None
# -----------------------------
# Filter for major leagues to keep it manageable and relevant
LEAGUES = {
    39: "Premier League",
    40: "Championship",
    41: "League One",
    42: "League Two",
    140: "La Liga",
    141: "La Liga 2",
    78: "Bundesliga",
    79: "2. Bundesliga",
    135: "Serie A",
    136: "Serie B",
    61: "Ligue 1",
    62: "Ligue 2"
}
WEEKS_BACK = 10

def get_weekend_dates(num_weeks):
    """Get list of Saturday/Sunday dates for the last N weeks."""
    dates = []
    today = datetime.date.today()
    
    # Logic to find the most recent past Sunday
    days_since_sunday = (today.weekday() + 1) % 7
    if days_since_sunday == 0: # It is Sunday, but let's look at *past* weekends primarily
         days_since_sunday = 7
         
    last_sunday = today - datetime.timedelta(days=days_since_sunday)
    
    for i in range(num_weeks):
        sunday = last_sunday - datetime.timedelta(weeks=i)
        saturday = sunday - datetime.timedelta(days=1)
        dates.append(saturday)
        dates.append(sunday)
        
    return sorted(dates)

def fetch_predictions(date):
    """Fetch analysis from API. returns list of match objects."""
    try:
        # API uses query params: /api/analysis?date=YYYY-MM-DD
        url = f"{API_URL}?date={date.isoformat()}"
        with urllib.request.urlopen(url, timeout=20) as response:
            if response.status == 200:
                data = json.loads(response.read().decode('utf-8'))
                return data.get('matches', [])
            else:
                print(f"Error {response.status} for {date}")
                return []
    except Exception as e:
        print(f"Connection error for {date}: {e}")
        return []

def evaluate_predictions(matches):
    """Evaluate predictions against actual results using new PredictionResponse structure."""
    results = []
    
    for m in matches:
        # Structure is flat now (Step 1066 check)
        # score is in 'result' field (e.g. "2-1") or 'match_result'?
        # Verify script output says "result": null.
        # Assuming for past matches "result": "2-1".
        
        result_obj = m.get('result')
        if not result_obj or not isinstance(result_obj, dict):
            # Try 'match_result' or fallback if flat string
            if isinstance(result_obj, str) and ':' in result_obj:
                score_str = result_obj
            else:
                continue
        else:
            # It's a dict
            score_str = result_obj.get('actual_score') or result_obj.get('actualScore')
            
        if not score_str:
            score_str = lookup_excel_result(m.get('date'), m.get('home_team', ''), m.get('away_team', ''))

        if not score_str:
            continue

        try:
            # Score format in Handler: "H:A"
            parts = score_str.split(':')
            home_score = int(parts[0].strip())
            away_score = int(parts[1].strip())
        except:
            continue
            
        league_name = m.get('league')
        date_str = m.get('date')
        
        # New Structure uses 'prediction' root object
        pred_root = m.get('prediction')
        if not pred_root:
            continue
            
        total_goals = home_score + away_score
        
        # --- Over 2.5 ---
        p_over25 = pred_root.get('over25', {})
        val_over25 = p_over25.get('prediction', False)
        qual_over25 = p_over25.get('is_qualified', False)
        prob_over25 = p_over25.get('probability', 0.0)
        
        actual_over25 = total_goals > 2.5
        correct_over25 = (val_over25 == actual_over25)
        
        # --- BTTS ---
        p_btts = pred_root.get('btts', {})
        val_btts = p_btts.get('prediction', False)
        qual_btts = p_btts.get('is_qualified', False)
        prob_btts = p_btts.get('probability', 0.0)
        
        actual_btts = (home_score > 0 and away_score > 0)
        correct_btts = (val_btts == actual_btts)
        
        # --- Match Winner ---
        p_winner = pred_root.get('match_winner', {})
        val_winner = p_winner.get('prediction', 'unknown') # "home", "draw", "away"
        qual_winner = p_winner.get('is_qualified', False)
        conf_winner = p_winner.get('confidence', 0.0)
        
        if home_score > away_score: actual_winner = "home"
        elif home_score == away_score: actual_winner = "draw"
        else: actual_winner = "away"
        
        correct_winner = (val_winner.lower() == actual_winner)
        
        # --- Low Scoring ---
        # "Low Scoring" signal implies Under 1.5 Goals (0-0, 1-0, 0-1)
        # Prediction is boolean (True = Low Scoring).
        p_low = pred_root.get('low_scoring', {})
        val_low = p_low.get('prediction', False) # True if signal active
        qual_low = p_low.get('is_qualified', False)
        prob_low = p_low.get('probability', 0.0) # Probability of Under 1.5
        
        actual_low = total_goals <= 1
        # If signal is active (True), we expect actual_low (True).
        # Use simple equality for general accuracy, but focused stats usually look at 'Qualified' subset.
        correct_low = (val_low == actual_low)

        results.append({
            'date': date_str,
            'league': league_name,
            'home': m.get('home_team'),
            'away': m.get('away_team'),
            'score': f"{home_score}-{away_score}",
            
            'over25_correct': correct_over25,
            'over25_prob': prob_over25,
            'over25_qualified': qual_over25,
            
            'btts_correct': correct_btts,
            'btts_prob': prob_btts,
            'btts_qualified': qual_btts,
            
            'winner_correct': correct_winner,
            'winner_conf': conf_winner,
            'winner_qualified': qual_winner,
            'predicted_winner': val_winner.lower(),
            'actual_winner': actual_winner,

            'low_correct': correct_low,
            'low_prob': prob_low,
            'low_qualified': qual_low
        })
        
    return results

def main():
    print("Initializing Database Fallbacks...")
    load_excel_fallback()
    print(f"Starting Analysis Backtest for last {WEEKS_BACK} weeks...")
    dates = get_weekend_dates(WEEKS_BACK)
    print(f"Dates to check ({len(dates)} days): {[d.isoformat() for d in dates]}")
    
    all_results = []
    
    for d in dates:
        print(f"Fetching {d}...", end='', flush=True)
        matches = fetch_predictions(d)
        print(f" Found {len(matches)} matches.", end='', flush=True)
        
        if matches:
            eval_results = evaluate_predictions(matches)
            all_results.extend(eval_results)
            print(f" Evaluated {len(eval_results)}.")
        else:
            print(" No data.")
            
        time.sleep(0.1)

    if not all_results:
        print("No finished matches found with predictions.")
        return

    df = pd.DataFrame(all_results)
    
    total = len(df)
    print("\n" + "="*60)
    print("BACKTEST RESULTS — 10 WEEK COMBINED PREDICTION")
    print("(Poisson 35% + MC 40% + ML 15% + Market 10%)")
    print("="*60)
    print(f"Total Matches Analyzed: {total}")
    
    # --- Overall (All Matches) ---
    print(f"\n{'─'*60}")
    print("ALL MATCHES (before decision layer):")
    print(f"{'─'*60}")
    for name, col in [("Over 2.5", "over25_correct"), ("BTTS", "btts_correct"), ("Winner", "winner_correct")]:
        correct = df[col].sum()
        wrong = total - correct
        acc = correct / total * 100
        print(f"  {name:12s}  ✅ {correct:>3} correct  ❌ {wrong:>3} wrong  ({acc:.1f}%)")
    
    # --- Professional Metrics (Log Loss + Brier) ---
    print(f"\n{'─'*60}")
    print("PROFESSIONAL METRICS:")
    print(f"{'─'*60}")
    
    import math
    
    def log_loss(predicted, actual):
        """Log loss — penalizes confident wrong predictions."""
        p = max(min(predicted, 1 - 1e-6), 1e-6)
        return -math.log(p) if actual else -math.log(1 - p)
    
    def brier_score(predicted, actual):
        """Brier score — measures probability calibration."""
        return (predicted - (1.0 if actual else 0.0)) ** 2
    
    prob_markets = [
        ("Over 2.5",  "over25_prob",  "over25_correct"),
        ("BTTS",      "btts_prob",    "btts_correct"),
    ]
    
    print(f"  {'Market':<12s}  {'Log Loss':>10s}  {'Brier':>10s}  {'Calibration'}")
    print(f"  {'─'*12}  {'─'*10}  {'─'*10}  {'─'*15}")
    
    for name, prob_col, correct_col in prob_markets:
        ll_values = []
        bs_values = []
        for _, row in df.iterrows():
            p = row[prob_col]
            actual = row[correct_col]
            if p > 0:
                ll_values.append(log_loss(p, actual))
                bs_values.append(brier_score(p, actual))
        
        if ll_values:
            avg_ll = sum(ll_values) / len(ll_values)
            avg_bs = sum(bs_values) / len(bs_values)
            # Calibration: <0.25 = good, <0.20 = very good
            cal = "🟢 Good" if avg_bs < 0.20 else ("🟡 OK" if avg_bs < 0.25 else "🔴 Poor")
            print(f"  {name:<12s}  {avg_ll:>10.4f}  {avg_bs:>10.4f}  {cal}")
    
    print(f"\n  Lower = better. Brier < 0.20 = good calibration.")
    
    # --- Match Winner Breakdown ---
    print(f"\n{'─'*60}")
    print("MATCH WINNER BREAKDOWN (All Matches):")
    print(f"{'─'*60}")
    for outcome in ['home', 'draw', 'away']:
        predicted = df[df['predicted_winner'] == outcome]
        if len(predicted) == 0:
            print(f"  {outcome.upper():6s}  0 predicted")
            continue
        correct = predicted['winner_correct'].sum()
        wrong = len(predicted) - correct
        acc = correct / len(predicted) * 100
        # Also show how many actual results of this type existed
        actual_count = len(df[df['actual_winner'] == outcome])
        print(f"  {outcome.upper():6s}  Predicted {len(predicted):>3}x  ✅ {correct:>3} correct  ❌ {wrong:>3} wrong  ({acc:.1f}%)  [actual {outcome}: {actual_count}]")
    
    # Qualified winner breakdown
    df_qw = df[df['winner_qualified']]
    if len(df_qw) > 0:
        print(f"\n  Qualified Winner Breakdown ({len(df_qw)} matches):")
        for outcome in ['home', 'draw', 'away']:
            predicted = df_qw[df_qw['predicted_winner'] == outcome]
            if len(predicted) == 0: continue
            correct = predicted['winner_correct'].sum()
            wrong = len(predicted) - correct
            acc = correct / len(predicted) * 100
            print(f"    {outcome.upper():6s}  Predicted {len(predicted):>3}x  ✅ {correct:>3} correct  ❌ {wrong:>3} wrong  ({acc:.1f}%)")

    # --- Decision Layer ---
    print(f"\n{'─'*60}")
    print("DECISION LAYER (Qualified Matches Only):")
    print(f"{'─'*60}")
    
    markets = [
        ("Over 2.5",      "over25_qualified",  "over25_correct"),
        ("BTTS",          "btts_qualified",     "btts_correct"),
        ("Match Winner",  "winner_qualified",   "winner_correct"),
        ("Low Scoring",   "low_qualified",      "low_correct"),
    ]
    
    for name, qual_col, correct_col in markets:
        qualified = df[df[qual_col]]
        q_count = len(qualified)
        if q_count == 0:
            print(f"  {name:12s}  0 qualified out of {total}")
            continue
        correct = qualified[correct_col].sum()
        wrong = q_count - correct
        acc = correct / q_count * 100
        rate = q_count / total * 100
        print(f"  {name:12s}  {q_count:>3} qualified ({rate:.1f}%)  →  ✅ {correct:>3} correct  ❌ {wrong:>3} wrong  ({acc:.1f}%)")
    
    # --- League Breakdown (Qualified Only) ---
    print(f"\n{'─'*60}")
    print("BY LEAGUE (Qualified Only):")
    print(f"{'─'*60}")
    
    headers = ['League', 'Over 2.5', 'BTTS', 'Winner']
    league_stats = []
    
    for league in sorted(df['league'].unique()):
        subset = df[df['league'] == league]
        if len(subset) < 5: continue
        
        sub_o25 = subset[subset['over25_qualified']]
        sub_btts = subset[subset['btts_qualified']]
        sub_winner = subset[subset['winner_qualified']]
        
        if len(sub_o25)+len(sub_btts)+len(sub_winner) < 2:
            continue
            
        row = [league]
        
        def fmt_stat(sub, col):
            if sub.empty: return "-"
            c = sub[col].sum()
            w = len(sub) - c
            return f"{c}/{len(sub)} ({c/len(sub):.0%})"
            
        row.append(fmt_stat(sub_o25, 'over25_correct'))
        row.append(fmt_stat(sub_btts, 'btts_correct'))
        row.append(fmt_stat(sub_winner, 'winner_correct'))
        
        league_stats.append(row)
    
    print(tabulate(league_stats, headers=headers, tablefmt='grid'))

if __name__ == "__main__":
    main()

