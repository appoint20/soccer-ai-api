"""
Backtest API script.
Queries the Prediction API for the last 10 weeks and calculates accuracy stats.
"""

import requests
import datetime
import pandas as pd
from tabulate import tabulate
import time
import sys

# Configuration
API_URL = "http://localhost:5166/api/fixtures/predictions"  # Adjust port if needed
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
    # Start from last weekend
    current = today - datetime.timedelta(days=today.weekday()) - datetime.timedelta(days=2) # Go back to last Saturday ish
    
    # Adjust logic to find last Sunday
    days_since_sunday = (today.weekday() + 1) % 7
    last_sunday = today - datetime.timedelta(days=days_since_sunday)
    
    for i in range(num_weeks):
        sunday = last_sunday - datetime.timedelta(weeks=i)
        saturday = sunday - datetime.timedelta(days=1)
        dates.append(saturday)
        dates.append(sunday)
        
    return sorted(dates)

def fetch_predictions(date, league_id):
    """Fetch predictions from API."""
    try:
        response = requests.get(
            API_URL, 
            params={"date": date.isoformat(), "leagueId": league_id, "language": "en"},
            timeout=10
        )
        if response.status_code == 200:
            return response.json().get('predictions', [])
        else:
            print(f"Error {response.status_code} for {date} League {league_id}: {response.text}")
            return []
    except Exception as e:
        print(f"Connection error: {e}")
        return []

def evaluate_predictions(predictions):
    """Evaluate predictions against actual results."""
    results = []
    
    for p in predictions:
            
        home_goals = p.get('actual_home_goals')
        away_goals = p.get('actual_away_goals')
        
        if home_goals is None or away_goals is None:
            continue
            
        total_goals = home_goals + away_goals
        
        # Over 2.5
        actual_over25 = total_goals > 2.5
        pred_over25 = p['over25']['prediction']
        conf_over25 = p['over25']['confidence']
        
        # BTTS
        actual_btts = home_goals > 0 and away_goals > 0
        pred_btts = p['btts']['prediction']
        conf_btts = p['btts']['confidence']
        
        # HDA
        if home_goals > away_goals:
            actual_hda = "Home"
        elif home_goals == away_goals:
            actual_hda = "Draw"
        else:
            actual_hda = "Away"
            
        pred_hda = p['hda']['prediction']
        conf_hda = p['hda']['confidence']
        
        results.append({
            'date': p['match_date'],
            'home': p['home_team_name'],
            'away': p['away_team_name'],
            'over25_correct': actual_over25 == pred_over25,
            'over25_conf': conf_over25,
            'btts_correct': actual_btts == pred_btts,
            'btts_conf': conf_btts,
            'hda_correct': actual_hda == pred_hda,
            'hda_conf': conf_hda
        })
        
    return results

def main():
    print(f"Starting Backtest for last {WEEKS_BACK} weeks...")
    dates = get_weekend_dates(WEEKS_BACK)
    print(f"Dates to check: {[d.isoformat() for d in dates]}")
    
    all_results = []
    
    for d in dates:
        print(f"Processing {d}...")
        for league_id, league_name in LEAGUES.items():
            preds = fetch_predictions(d, league_id)
            if not preds:
                continue
                
            eval_results = evaluate_predictions(preds)
            for res in eval_results:
                res['league'] = league_name
            all_results.extend(eval_results)
            
            # Rate limit slightly
            time.sleep(0.1)

    if not all_results:
        print("No finished matches found with predictions.")
        return

    df = pd.DataFrame(all_results)
    
    print("\n" + "="*50)
    print("BACKTEST RESULTS")
    print("="*50)
    print(f"Total Matches Analyzed: {len(df)}")
    
    # Overall Accuracy
    acc_over25 = df['over25_correct'].mean()
    acc_btts = df['btts_correct'].mean()
    acc_hda = df['hda_correct'].mean()
    
    print(f"\nOverall Accuracy:")
    print(f"  Over 2.5: {acc_over25:.2%}")
    print(f"  BTTS:     {acc_btts:.2%}")
    print(f"  H/D/A:    {acc_hda:.2%}")
    
    # High Confidence Accuracy (>60%)
    df_high = df[df['over25_conf'] > 0.6]
    if not df_high.empty:
        print(f"\nHigh Confidence (>60%) Over 2.5 Accuracy: {df_high['over25_correct'].mean():.2%} ({len(df_high)} matches)")
        
    df_high_btts = df[df['btts_conf'] > 0.6]
    if not df_high_btts.empty:
        print(f"High Confidence (>60%) BTTS Accuracy:     {df_high_btts['btts_correct'].mean():.2%} ({len(df_high_btts)} matches)")

    # By League
    league_stats = df.groupby('league').agg({
        'over25_correct': 'mean',
        'btts_correct': 'mean',
        'hda_correct': 'mean',
        'date': 'count'
    }).reset_index()
    
    print("\nAccuracy by League:")
    print(tabulate(league_stats, headers=['League', 'Over 2.5', 'BTTS', 'H/D/A', 'Count'], tablefmt='grid', floatfmt=".1%"))

if __name__ == "__main__":
    main()
