
import requests
import json

# Configuration
API_URL = "http://localhost:5166/api/combinations"
STAKE = 10
DATES = ['2026-01-30', '2026-02-01', '2026-02-02']

def check_win(match):
    status = match.get('status')
    if status != 'FT':
        return False, "Not Finished"
    
    home_goals = match.get('actual_home_goals')
    away_goals = match.get('actual_away_goals')
    
    if home_goals is None or away_goals is None:
        return False, "No Result"

    prediction = match.get('prediction')
    market = match.get('market')
    
    if market == "Over 2.5 Goals" and prediction == "Over":
        return (home_goals + away_goals) > 2.5, f"{home_goals}-{away_goals}"
    if market == "Both Teams To Score" and prediction == "Yes":
        return (home_goals > 0) and (away_goals > 0), f"{home_goals}-{away_goals}"
    if market == "Match Winner":
        if prediction == "Home": return home_goals > away_goals, f"{home_goals}-{away_goals}"
        if prediction == "Draw": return home_goals == away_goals, f"{home_goals}-{away_goals}"
        if prediction == "Away": return away_goals > home_goals, f"{home_goals}-{away_goals}"
            
    return False, "Unknown Market"

def run_check():
    total_invested = 0
    total_returned = 0
    wins = 0
    
    for date_str in DATES:
        print(f"\n--- Checking {date_str} ---")
        try:
            response = requests.get(f"{API_URL}?date={date_str}&language=en")
            data = response.json()
            matches = data.get('matches', [])
            
            if not matches:
                print("No combination found (likely no matches or insufficient data).")
                continue
                
            total_invested += STAKE
            print(f"Combination found (1 per day). Stake: €{STAKE}")
            
            legs_won = 0
            combo_odds = 1.0
            
            for m in matches:
                won, score = check_win(m)
                if won: legs_won += 1
                
                raw_odds = m.get('odds', 0)
                if raw_odds > 50: raw_odds = raw_odds / 100.0
                if raw_odds < 1.01: raw_odds = 1.6
                
                combo_odds *= raw_odds
                print(f"  {m['home_team']} vs {m['away_team']} | {m['market']}={m['prediction']} | Odds:{raw_odds:.2f} | Score:{score} -> {'✅' if won else '❌'}")

            if legs_won == len(matches) and len(matches) > 0:
                payout = STAKE * combo_odds
                total_returned += payout
                wins += 1
                print(f"  RESULT: WON! 🏆 Payout: €{payout:.2f}")
            else:
                print(f"  RESULT: LOST. ({legs_won}/{len(matches)} legs)")

        except Exception as e:
            print(f"Error: {e}")

    profit = total_returned - total_invested
    roi = (profit / total_invested * 100) if total_invested > 0 else 0
    
    print("\n" + "="*40)
    print(f"Invested: €{total_invested:.2f}")
    print(f"Returned: €{total_returned:.2f}")
    print(f"Profit:   €{profit:.2f}")
    print(f"ROI:      {roi:.2f}%")
    print("="*40)

if __name__ == "__main__":
    run_check()
