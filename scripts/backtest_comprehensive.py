import urllib.request
import json
import datetime
import pandas as pd
from tabulate import tabulate
import os

API_URL = "http://localhost:5165/api/analysis"
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
            print(f"Loaded Excel fallback ({len(FALLBACK_DF)} matches).")
    except Exception as e:
        print(f"Excel fallback error: {e}")

def lookup_excel_result(date_str, home_team, away_team):
    if FALLBACK_DF is None or FALLBACK_DF.empty or not date_str: return None
    date_part = date_str.split('T')[0]
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

def fetch_predictions(date):
    url = f"{API_URL}?date={date.isoformat()}"
    try:
        with urllib.request.urlopen(url, timeout=10) as response:
            if response.status == 200:
                return json.loads(response.read().decode('utf-8')).get('matches', [])
    except Exception:
        pass
    return []

def run_backtest():
    load_excel_fallback()
    
    today = datetime.date.today()
    start_date = today - datetime.timedelta(weeks=10)
    
    # 2026-02-21 and 2026-02-22 are this past weekend based on today = 2026-02-23
    this_weekend_dates = [(today - datetime.timedelta(days=2)).isoformat(), (today - datetime.timedelta(days=1)).isoformat()]
    
    stats = []
    gemini_stats = []

    current_date = start_date
    while current_date <= today:
        print(f"Fetching {current_date}...")
        matches = fetch_predictions(current_date)
        is_weekend = current_date.weekday() >= 5
        date_str = current_date.isoformat()
        
        for m in matches:
            # Parse Result
            result_obj = m.get('result')
            score_str = None
            if isinstance(result_obj, dict):
                score_str = result_obj.get('actual_score') or result_obj.get('actualScore')
            elif isinstance(result_obj, str) and ':' in result_obj:
                score_str = result_obj
                
            if not score_str:
                score_str = lookup_excel_result(m.get('date'), m.get('home_team', ''), m.get('away_team', ''))
                
            if not score_str: continue
            
            try:
                parts = score_str.split(':')
                home_score, away_score = int(parts[0].strip()), int(parts[1].strip())
            except:
                continue

            total_goals = home_score + away_score
            actual_btts = home_score > 0 and away_score > 0
            actual_over25 = total_goals > 2.5
            actual_under25 = total_goals <= 2.5
            if home_score > away_score: actual_winner = "home"
            elif home_score == away_score: actual_winner = "draw"
            else: actual_winner = "away"
            
            league = m.get('league', 'Unknown')
            pred_root = m.get('prediction', {})
            
            # --- OVERALL STATS (Uses Qualified Predictions Only) ---
            # BTTS
            if pred_root.get('btts', {}).get('is_qualified'):
                pred = pred_root['btts'].get('prediction', False)
                stats.append({'league': league, 'market': 'BTTS', 'is_weekend': is_weekend, 'correct': pred == actual_btts})
            # Over 2.5
            if pred_root.get('over25', {}).get('is_qualified'):
                pred = pred_root['over25'].get('prediction', False)
                stats.append({'league': league, 'market': 'Over 2.5', 'is_weekend': is_weekend, 'correct': pred == actual_over25})
            # Match Winner
            if pred_root.get('match_winner', {}).get('is_qualified'):
                pred = pred_root['match_winner'].get('prediction', '').lower()
                stats.append({'league': league, 'market': 'Match Winner', 'is_weekend': is_weekend, 'correct': pred == actual_winner})
            # Low Scoring
            if pred_root.get('low_scoring', {}).get('is_qualified'):
                pred = pred_root['low_scoring'].get('prediction', False)
                stats.append({'league': league, 'market': 'Low Scoring', 'is_weekend': is_weekend, 'correct': pred == (total_goals <= 1)})

            # --- GEMINI STATS (THIS WEEKEND ONLY) ---
            if date_str in this_weekend_dates:
                gemini = m.get('gemini')
                if gemini and gemini.get('recommendation'):
                    rec = gemini['recommendation'].lower()
                    correct = False
                    if "btts" in rec: correct = actual_btts
                    elif "over 2.5" in rec: correct = actual_over25
                    elif "under 2.5" in rec: correct = actual_under25
                    elif "home" in rec: correct = (actual_winner == "home")
                    elif "away" in rec: correct = (actual_winner == "away")
                    elif "draw" in rec: correct = (actual_winner == "draw")
                    
                    if "avoid" not in rec and "trap" not in rec:
                         gemini_stats.append({'market': rec, 'correct': correct})
                         
        current_date += datetime.timedelta(days=1)

    df = pd.DataFrame(stats)
    
    print("\n" + "="*50)
    print("10 WEEK BACKTEST REPORT (Qualified Selections Only)")
    print("="*50)
    
    if df.empty:
        print("No qualified predictions found.")
    else:
        print(f"\nOverall Accuracy: {df['correct'].mean()*100:.1f}% ({df['correct'].sum()}/{len(df)})")
        
        print("\n--- By Market ---")
        market_acc = df.groupby('market')['correct'].agg(['mean', 'count', 'sum']).reset_index()
        market_acc.columns = ['Market', 'Win Rate', 'Total Bets', 'Won']
        market_acc['Win Rate'] = (market_acc['Win Rate'] * 100).round(1).astype(str) + '%'
        print(tabulate(market_acc, headers='keys', tablefmt='psql', showindex=False))
        
        print("\n--- By League ---")
        league_acc = df.groupby('league')['correct'].agg(['mean', 'count', 'sum']).sort_values('count', ascending=False).reset_index()
        league_acc.columns = ['League', 'Win Rate', 'Total Bets', 'Won']
        league_acc['Win Rate'] = (league_acc['Win Rate'] * 100).round(1).astype(str) + '%'
        print(tabulate(league_acc, headers='keys', tablefmt='psql', showindex=False))

        print("\n--- Weekend vs Weekday ---")
        day_acc = df.groupby('is_weekend')['correct'].agg(['mean', 'count', 'sum']).reset_index()
        day_acc['Day Type'] = day_acc['is_weekend'].map({True: 'Weekend', False: 'Weekday'})
        day_acc = day_acc.drop(columns=['is_weekend'])
        day_acc.columns = ['Win Rate', 'Total Bets', 'Won', 'Day Type']
        # Reorder columns
        day_acc = day_acc[['Day Type', 'Win Rate', 'Total Bets', 'Won']]
        day_acc['Win Rate'] = (day_acc['Win Rate'] * 100).round(1).astype(str) + '%'
        print(tabulate(day_acc, headers='keys', tablefmt='psql', showindex=False))

    print("\n" + "="*50)
    print("GEMINI PERFORMANCE (THIS PAST WEEKEND ONLY)")
    print("="*50)
    gdf = pd.DataFrame(gemini_stats)
    if gdf.empty:
        print("No Gemini recommendations found for this weekend.")
    else:
        print(f"\nOverall Gemini Accuracy: {gdf['correct'].mean()*100:.1f}% ({gdf['correct'].sum()}/{len(gdf)})")
        g_acc = gdf.groupby('market')['correct'].agg(['mean', 'count', 'sum']).reset_index()
        g_acc.columns = ['Recommendation', 'Win Rate', 'Total Bets', 'Won']
        g_acc['Win Rate'] = (g_acc['Win Rate'] * 100).round(1).astype(str) + '%'
        print(tabulate(g_acc, headers='keys', tablefmt='psql', showindex=False))

if __name__ == "__main__":
    run_backtest()
