import urllib.request
import json
from datetime import datetime, timedelta

def get_combinations(date_str):
    url = f"http://localhost:5165/api/combinations?date={date_str}&language=en"
    try:
        with urllib.request.urlopen(url) as response:
            if response.status == 200:
                data = json.loads(response.read().decode('utf-8'))
                return data.get('combinations', [])
    except Exception as e:
        print(f"Error fetching {date_str}: {e}")
    return []

def check_result(market, prediction, home_goals, away_goals):
    if home_goals is None or away_goals is None:
        return "Pending"
    
    total_goals = home_goals + away_goals
    
    if market == "Over 2.5 Goals":
        return "Win" if total_goals > 2.5 else "Loss"
    elif market == "Under 2.5 Goals":
        return "Win" if total_goals < 2.5 else "Loss"
    elif market == "Both Teams To Score":
        if prediction == "Yes":
            return "Win" if home_goals > 0 and away_goals > 0 else "Loss"
        else: # "No"
            return "Win" if home_goals == 0 or away_goals == 0 else "Loss"
    elif market == "Match Winner":
        if prediction == "Home":
            return "Win" if home_goals > away_goals else "Loss"
        elif prediction == "Away":
            return "Win" if away_goals > home_goals else "Loss"
        elif prediction == "Draw":
            return "Win" if home_goals == away_goals else "Loss"
            
    return "Unknown Market"

def run_backtest(weeks=2):
    end_date = datetime.now()
    start_date = end_date - timedelta(weeks=weeks)
    current_date = start_date
    
    print(f"Combinations from {start_date.strftime('%Y-%m-%d')} to {end_date.strftime('%Y-%m-%d')}")
    print("=" * 60)
    
    total_combos = 0
    total_won = 0
    
    while current_date <= end_date:
        date_str = current_date.strftime('%Y-%m-%d')
        combos = get_combinations(date_str)
        
        if combos:
            # print(f"\n📅 {date_str}")
            pass
            
        for combo in combos:
            combo_name = combo.get('name', 'Unknown')
            matches = combo.get('matches', [])
            
            if not matches:
                continue
                
            all_won = True
            pending = False
            combined_odds = 1.0
            match_details = []
            
            for m in matches:
                market = m.get('market')
                prediction = m.get('prediction')
                home_team = m.get('home_team')
                away_team = m.get('away_team')
                
                home_goals = m.get('actual_home_goals')
                away_goals = m.get('actual_away_goals')
                status = m.get('status')
                
                odds = m.get('odds', 0.0)
                if odds > 50: odds /= 100.0
                if odds < 1.01: odds = 1.0
                
                combined_odds *= odds
                
                if status not in ['FT', 'AET', 'PEN']:
                    pending = True
                    res = "Pending"
                else:
                    res = check_result(market, prediction, home_goals, away_goals)
                    if res != "Win":
                        all_won = False
                
                score = f"{home_goals}-{away_goals}" if status == 'FT' else "?-?"
                match_details.append(f"{home_team} vs {away_team} ({market}: {prediction}) [{score}] -> {res}")
            
            total_combos += 1
            if all_won and not pending:
                total_won += 1
                status_icon = "✅ WIN"
            elif pending:
                status_icon = "⏳ PENDING"
            else:
                status_icon = "❌ LOSS"
                
            print(f"\n{status_icon} [{date_str}] {combo_name} (Odds: {combined_odds:.2f})")
            for d in match_details:
                print(f"   - {d}")

        current_date += timedelta(days=1)

if __name__ == "__main__":
    run_backtest(weeks=2)
