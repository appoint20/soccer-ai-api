
import requests
import json
from datetime import datetime, timedelta

# Configuration
API_URL = "http://localhost:5166/api/combinations"
WEEKS_BACK = 10
STAKE = 25  # Changed to 25 EUR per combination

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
    
    home_goals = match.get('actual_home_goals')
    away_goals = match.get('actual_away_goals')
    
    if home_goals is None or away_goals is None: return False

    prediction = match.get('prediction')
    market = match.get('market')
    
    if market == "Over 2.5 Goals" and prediction == "Over":
        return (home_goals + away_goals) > 2.5
    if market == "Under 2.5 Goals":
        return (home_goals + away_goals) < 2.5
    if market == "2-3 Goals":
        total = home_goals + away_goals
        return total == 2 or total == 3
    if market == "Both Teams To Score" and prediction == "Yes":
        return (home_goals > 0) and (away_goals > 0)
    if market == "Over 2.5 & BTTS" and prediction == "Yes":
        return ((home_goals + away_goals) > 2.5) and ((home_goals > 0) and (away_goals > 0))
    if market == "Match Winner":
        if prediction == "Home": return home_goals > away_goals
        if prediction == "Draw": return home_goals == away_goals
        if prediction == "Away": return away_goals > home_goals
            
    return False

def run_backtest():
    dates = get_weekend_dates(WEEKS_BACK)
    print(f"Starting Multi-Combo Backtest (Stake €{STAKE})...")
    
    total_invested = 0
    total_returned = 0
    total_combos = 0
    total_wins = 0
    
    for date_str in dates:
        print(f"\nProcessing {date_str}...", end=" ", flush=True)
        try:
            response = requests.get(f"{API_URL}?date={date_str}&language=en")
            if response.status_code != 200:
                print(f"Error {response.status_code}")
                continue
                
            data = response.json()
            combinations = data.get('combinations', [])
            
            if not combinations:
                print("No combinations.")
                continue

            print(f"Found {len(combinations)} combos.")
            
            for combo in combinations:
                matches = combo.get('matches', [])
                name = combo.get('name', 'Unknown')
                
                # Verify finished
                if any(m.get('status') != 'FT' for m in matches):
                    # print(f"  {name}: Pending/Void")
                    continue
                
                # Determine Stake based on Group
                current_stake = 50
                if name.startswith("Goal Combo"):
                    current_stake = 100
                elif name.startswith("Win/Mix Combo"):
                    current_stake = 50
                
                total_invested += current_stake
                total_combos += 1
                
                legs_won = 0
                combo_odds = 1.0
                
                for m in matches:
                    if check_win(m): legs_won += 1
                    
                    raw_odds = m.get('odds', 0)
                    if raw_odds > 50: raw_odds /= 100.0
                    if raw_odds < 1.01: raw_odds = 1.6
                    combo_odds *= raw_odds
                
                if legs_won == 3:
                    payout = current_stake * combo_odds
                    total_returned += payout
                    total_wins += 1
                    print(f"  ✅ {name} (€{current_stake}): WON! Odds:{combo_odds:.2f} Pay:€{payout:.2f}")
                else:
                    print(f"  ❌ {name} (€{current_stake}): LOST ({legs_won}/3)")

        except Exception as e:
            print(f"Error: {e}")

    profit = total_returned - total_invested
    roi = (profit / total_invested * 100) if total_invested > 0 else 0

    print("\n" + "="*50)
    print("MULTI-COMBINATION BACKTEST RESULTS")
    print("="*50)
    print(f"Total Weekends: {WEEKS_BACK}")
    print(f"Total Combinations Placed: {total_combos}")
    print(f"Wins: {total_wins}")
    print(f"Losses: {total_combos - total_wins}")
    print(f"Strike Rate: {(total_wins / total_combos * 100) if total_combos > 0 else 0:.1f}%")
    print("-" * 30)
    print("-" * 30)
    print("Stake: €100 (Goal Group) / €50 (Mix Group)")
    print(f"Total Invested:  €{total_invested:.2f}")
    print(f"Total Returned:  €{total_returned:.2f}")
    print(f"Net Profit:      €{profit:.2f}")
    print(f"ROI:             {roi:.2f}%")
    print("="*50)

if __name__ == "__main__":
    run_backtest()
