
import requests
import json
from datetime import datetime, timedelta

# Configuration
API_URL = "http://localhost:5165/api/combinations"
WEEKS_BACK = 10
OUTPUT_FILE = "/Users/shivm/.gemini/antigravity/brain/4d145a37-dcf9-4de1-8041-2a4301b458d4/loss_analysis.md"

def get_weekend_dates(weeks_back=10):
    dates = []
    today = datetime.now()
    idx = (today.weekday() + 2) % 7
    saturday = today - timedelta(days=idx)
    current = saturday - timedelta(weeks=1) 
    
    for _ in range(weeks_back):
        dates.append(current.strftime('%Y-%m-%d'))
        dates.append((current + timedelta(days=1)).strftime('%Y-%m-%d'))
        current -= timedelta(weeks=1)
        
    return sorted(dates)

def check_win(match):
    status = match.get('status')
    if status != 'FT': return False
    
    h = match.get('actual_home_goals')
    a = match.get('actual_away_goals')
    if h is None or a is None: return False

    pred = match.get('prediction')
    mkt = match.get('market')
    
    if mkt == "Over 2.5 Goals" and pred == "Over": return (h + a) > 2.5
    if mkt == "Under 2.5 Goals" and pred == "Under": return (h + a) < 2.5
    if mkt == "Both Teams To Score" and pred == "Yes": return (h > 0) and (a > 0)
    if mkt == "2-3 Goals" and pred == "Yes": return (h + a) in [2, 3]
    if mkt == "Match Winner":
        if pred == "Home": return h > a
        if pred == "Draw": return h == a
        if pred == "Away": return a > h
            
    return False

def main():
    dates = get_weekend_dates(WEEKS_BACK)
    losses = []
    
    print(f"Analyzing losses for last {WEEKS_BACK} weeks...")
    
    for date_str in dates:
        try:
            response = requests.get(f"{API_URL}?date={date_str}&language=en")
            if response.status_code != 200: continue
            
            data = response.json()
            combinations = data.get('combinations', [])
            
            for combo in combinations:
                matches = combo.get('matches', [])
                if not matches or any(m.get('status') != 'FT' for m in matches): continue
                
                legs_won = 0
                match_results = []
                
                for m in matches:
                    won = check_win(m)
                    if won: legs_won += 1
                    
                    match_results.append({
                        "fixture": f"{m['home_team']} vs {m['away_team']}",
                        "market": m['market'],
                        "prediction": m['prediction'],
                        "score": f"{m.get('actual_home_goals')} - {m.get('actual_away_goals')}",
                        "odds": m.get('odds', 0),
                        "result": "WON" if won else "LOST"
                    })
                
                if legs_won < len(matches):
                    losses.append({
                        "date": date_str,
                        "name": combo['name'],
                        "legs_won": f"{legs_won}/{len(matches)}",
                        "matches": match_results
                    })

        except Exception as e:
            print(f"Error processing {date_str}: {e}")

    # Write Report
    with open(OUTPUT_FILE, "w") as f:
        f.write("# Loss Analysis Report\n\n")
        f.write(f"**Total Lost Combinations:** {len(losses)}\n\n")
        f.write("This report details every combination that failed during the backtest period. Use this to identify patterns in failed predictions (e.g., specific leagues, markets, or odds ranges).\n\n")
        
        for loss in losses:
            f.write(f"## {loss['date']} - {loss['name']} ({loss['legs_won']})\n")
            f.write("| Fixture | Market | Prediction | Score | Odds | Result |\n")
            f.write("| :--- | :--- | :--- | :--- | :--- | :--- |\n")
            for m in loss['matches']:
                icon = "✅" if m['result'] == "WON" else "❌"
                # Handle Odds normalization for display
                odds_disp = m['odds']
                if odds_disp > 50: odds_disp /= 100.0
                
                f.write(f"| {m['fixture']} | {m['market']} | {m['prediction']} | {m['score']} | {odds_disp:.2f} | {icon} |\n")
            f.write("\n")
            
    print(f"Report generated at {OUTPUT_FILE}")

if __name__ == "__main__":
    main()
