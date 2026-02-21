
import requests
import json
from datetime import datetime

# Configuration
API_URL = "http://localhost:5165/api/combinations"
DATES = ["2026-01-30", "2026-01-31", "2026-02-01", "2026-02-02"]

def check_win(match):
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
        if prediction == "Yes": return (home_goals > 0) and (away_goals > 0)
        return (home_goals == 0) or (away_goals == 0)
    if market == "2-3 Goals":
        total = home_goals + away_goals
        return total == 2 or total == 3
    if market == "Match Winner":
        if prediction == "Home": return home_goals > away_goals
        if prediction == "Draw": return home_goals == away_goals
        if prediction == "Away": return away_goals > home_goals
    return False

def audit():
    grand_total = 0
    grand_correct = 0

    for date_str in DATES:
        print(f"\n{'='*70}")
        print(f" DATE: {date_str}")
        print(f"{'='*70}")
        
        try:
            response = requests.get(f"{API_URL}?date={date_str}&language=en")
            if response.status_code != 200:
                print(f"Error {response.status_code}")
                continue
                
            data = response.json()
            combinations = data.get('combinations', [])
            
            day_total = 0
            day_correct = 0
            
            for i, combo in enumerate(combinations):
                matches = combo.get('matches', [])
                if not matches or any(m.get('status') != 'FT' for m in matches):
                    continue
                
                day_total += 1
                legs_won = sum(1 for m in matches if check_win(m))
                is_win = (legs_won == len(matches))
                if is_win: day_correct += 1
                
                result_str = "WIN ✅" if is_win else "LOSS ❌"
                odds = combo.get('total_odds', 1.0)
                # Handle cases where odds might be whole numbers (e.g. 520 for 5.20)
                if odds > 50: odds = odds / 100.0
                
                print(f"\n{combo.get('name', f'Combo #{i+1}'):<20} | {result_str} | Total Odds: {odds:.2f}")
                
                for m in matches:
                    win_icon = "✅" if check_win(m) else "❌"
                    home = m.get('home_team', 'Unknown')
                    away = m.get('away_team', 'Unknown')
                    market = m.get('market', '')
                    pred = m.get('prediction', '')
                    score = f"{m.get('actual_home_goals')}-{m.get('actual_away_goals')}"
                    print(f"  - {win_icon} {home} vs {away} | {market}: {pred} | Score: {score}")
            
            win_pct = (day_correct / day_total * 100) if day_total > 0 else 0
            print(f"\nSummary for {date_str}: {day_correct}/{day_total} correct ({win_pct:.1f}%)")
            
            grand_total += day_total
            grand_correct += day_correct

        except Exception as e:
            print(f"Error auditing {date_str}: {e}")

    print(f"\n{'#'*70}")
    grand_pct = (grand_correct / grand_total * 100) if grand_total > 0 else 0
    print(f" GRAND TOTAL: {grand_correct}/{grand_total} correct ({grand_pct:.1f}%)")
    print(f"{'#'*70}")

if __name__ == "__main__":
    audit()
