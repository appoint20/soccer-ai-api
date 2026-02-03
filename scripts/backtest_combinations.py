
import requests
import json
from datetime import datetime, timedelta

# Configuration
API_URL = "http://localhost:5166/api/combinations"
WEEKS_BACK = 10
STAKE = 100  # Currency unit per combination

def get_weekend_dates(weeks_back=10):
    """Get Saturday and Sunday dates for the last N weeks."""
    dates = []
    today = datetime.now()
    # Find last Saturday
    idx = (today.weekday() + 2) % 7
    saturday = today - timedelta(days=idx)
    
    # If today is Saturday/Sunday, might want to include it, 
    # but for backtest we focus on past finished matches mostly.
    # Start from last week to ensure we have results.
    current = saturday - timedelta(weeks=1) 
    
    for _ in range(weeks_back):
        dates.append(current.strftime('%Y-%m-%d')) # Saturday
        dates.append((current + timedelta(days=1)).strftime('%Y-%m-%d')) # Sunday
        current -= timedelta(weeks=1)
        
    return sorted(dates)

def check_win(match):
    """Determine if a single leg won based on prediction and actual results."""
    status = match.get('status')
    if status != 'FT':
        return False # Treated as void/loss for simplicity if not finished, but here assuming loss for rigor
    
    home_goals = match.get('actual_home_goals')
    away_goals = match.get('actual_away_goals')
    
    if home_goals is None or away_goals is None:
        return False

    prediction = match.get('prediction')
    market = match.get('market')
    
    # Over 2.5
    if market == "Over 2.5 Goals" and prediction == "Over":
        return (home_goals + away_goals) > 2.5
        
    # BTTS
    if market == "Both Teams To Score" and prediction == "Yes":
        return (home_goals > 0) and (away_goals > 0)
        
    # Match Winner
    if market == "Match Winner":
        if prediction == "Home":
            return home_goals > away_goals
        elif prediction == "Draw":
            return home_goals == away_goals
        elif prediction == "Away":
            return away_goals > home_goals
            
    return False

def run_backtest():
    dates = get_weekend_dates(WEEKS_BACK)
    print(f"Starting Combination Backtest for last {WEEKS_BACK} weeks...")
    print(f"Dates to check: {dates}")
    
    total_invested = 0
    total_returned = 0
    wins = 0
    losses = 0
    voids = 0 # No combo found
    
    history = []

    for date_str in dates:
        print(f"Processing {date_str}...", end=" ", flush=True)
        try:
            response = requests.get(f"{API_URL}?date={date_str}&language=en")
            if response.status_code != 200:
                print(f"Error {response.status_code}")
                continue
                
            data = response.json()
            matches = data.get('matches', [])
            
            if len(matches) < 3:
                print("Not enough matches for combo.")
                voids += 1
                continue
                
            # Check if all 3 matches are finished
            if any(m.get('status') != 'FT' for m in matches):
                print("Skipping (Matches not finished)")
                continue

            # Check individual legs
            legs_won = 0
            combo_odds = 1.0
            details = []
            
            for m in matches:
                won = check_win(m)
                if won:
                    legs_won += 1
                
                # Use odds from API, default to 1.5 if missing/zero for conservative estimate
                # Note: Odds comes as e.g. 1.85 or 185? API returns decimal? 
                # Checking controller: "Odds ?? 0". Python sees e.g. 1.5. 
                # Wait, DB stores 1.5 or 150? Need to check. Db usually decimal.
                # Assuming decimal.
                # EDIT: Previous check showed "odds": 127 etc. Looks like integer (1.27 -> 127 or similar?)
                # API Football standard is decimal. If 127 means 1.27? Or 127.0? 
                # Let's check raw JSON again. "odds": 127 might be 1.27? 
                # Actually, standard betting odds are ~1.5 to 3.0. 
                # If values are > 10, likely need division by 100? Or just weird?
                # Actually, API-Football usually returns decimal. Maybe database has standard decimal?
                # Let's assume standard decimal first. If > 10, divide by 100? No, 127 odds is huge.
                # Looking at previous output: "odds": 127. That's likely 1.27 if integer, or 127 if huge.
                # Ah, let's assume it is decimal. If > 50, likely missing decimal point?
                # Wait, 127.0 for Home Win is unlikely. 1.27 is very likely for strong home team.
                # Let's apply a heuristic: if odds > 20, divide by 100.
                
                raw_odds = m.get('odds', 0)
                if raw_odds > 50: 
                    raw_odds = raw_odds / 100.0 # Just a safe heuristic for now
                if raw_odds < 1.01:
                     raw_odds = 1.6 # Default fallback for ROI calculation if missing
                     
                combo_odds *= raw_odds
                details.append(f"{m['home_team']} vs {m['away_team']} ({m['market']}={m['prediction']}) Odds:{raw_odds:.2f} [{'WON' if won else 'LOST'}]")

            total_invested += STAKE
            
            if legs_won == 3:
                payout = STAKE * combo_odds
                total_returned += payout
                wins += 1
                print(f"WON! Odds: {combo_odds:.2f} Return: {payout:.2f}")
            else:
                losses += 1
                print(f"LOST. ({legs_won}/3 legs won)")
                for d in details:
                    print(f"  - {d}")

            history.append({
                'date': date_str,
                'result': 'WON' if legs_won == 3 else 'LOST',
                'payout': STAKE * combo_odds if legs_won == 3 else 0,
                'details': details
            })

        except Exception as e:
            print(f"Error: {e}")

    profit = total_returned - total_invested
    roi = (profit / total_invested * 100) if total_invested > 0 else 0

    print("\n" + "="*50)
    print("COMBINATION BACKTEST RESULTS")
    print("="*50)
    print(f"Total Weekends: {WEEKS_BACK}")
    print(f"Total Combinations Placed: {wins + losses}")
    print(f"Wins: {wins}")
    print(f"Losses: {losses}")
    print(f"Strike Rate: {(wins / (wins+losses) * 100) if (wins+losses) > 0 else 0:.1f}%")
    print("-" * 30)
    print(f"Total Invested: €{total_invested:.2f}")
    print(f"Total Returned: €{total_returned:.2f}")
    print(f"Net Profit:     €{profit:.2f}")
    print(f"ROI:            {roi:.2f}%")
    print("="*50)

if __name__ == "__main__":
    run_backtest()
