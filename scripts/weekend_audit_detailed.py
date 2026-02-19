
import requests
import json
from datetime import datetime

# Configuration
API_URL = "http://localhost:5165/api/combinations"
DATES = ["2026-01-30", "2026-01-31", "2026-02-01", "2026-02-02"]

def check_win(match):
    status = match.get('status')
    if status != 'FT':
        return False
    
    home_goals = match.get('actual_home_goals')
    away_goals = match.get('actual_away_goals')
    
    if home_goals is None or away_goals is None:
        return False

    prediction = match.get('prediction')
    market = match.get('market')
    
    if market == "Over 2.5 Goals":
        return (home_goals + away_goals) > 2.5
    if market == "Under 2.5 Goals":
        return (home_goals + away_goals) < 2.5
    if market == "Both Teams To Score":
        return (home_goals > 0) and (away_goals > 0)
    if market == "2-3 Goals":
        total = home_goals + away_goals
        return total == 2 or total == 3
    if market == "Match Winner":
        if prediction == "Home": return home_goals > away_goals
        if prediction == "Draw": return home_goals == away_goals
        if prediction == "Away": return away_goals > home_goals
    return False

def audit():
    for date_str in DATES:
        print(f"\n{'='*60}")
        print(f"DATE: {date_str}")
        print(f"{'='*60}")
        
        try:
            response = requests.get(f"{API_URL}?date={date_str}&language=en")
            if response.status_code != 200:
                print(f"Error {response.status_code}")
                continue
                
            data = response.json()
            combinations = data.get('combinations', [])
            
            if not combinations:
                print("No combinations found.")
                continue

            for i, combo in enumerate(combinations, 1):
                matches = combo.get('matches', [])
                if not matches or any(m.get('status') != 'FT' for m in matches):
                    continue
                
                legs_won = sum(1 for m in matches if check_win(m))
                status = "✅ WIN" if legs_won == len(matches) else "❌ LOSS"
                
                print(f"\nCombo #{i}: {combo.get('name')} [{status}]")
                print(f"{'-'*40}")
                for m in matches:
                    is_won = check_win(m)
                    win_marker = "✅" if is_won else "❌"
                    home = m.get('home_team')
                    away = m.get('away_team')
                    market = m.get('market')
                    pred = m.get('prediction')
                    res = f"{m.get('actual_home_goals')}-{m.get('actual_away_goals')}"
                    print(f"  {win_marker} {home} vs {away} | {market}: {pred} | Result: {res}")

        except Exception as e:
            print(f"Error: {e}")

if __name__ == "__main__":
    audit()
