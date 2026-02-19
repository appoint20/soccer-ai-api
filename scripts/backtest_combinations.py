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

def run_backtest(weeks=10):
    end_date = datetime.now()
    start_date = end_date - timedelta(weeks=weeks)
    current_date = start_date
    
    total_combos = 0
    total_won = 0
    total_staked = 0
    total_returned = 0
    
    print(f"Starting Backtest from {start_date.strftime('%Y-%m-%d')} to {end_date.strftime('%Y-%m-%d')}")
    print("-" * 60)
    
    results_by_type = {} # Track ROI per combo type (e.g., Goal Combo 1)

    while current_date <= end_date:
        date_str = current_date.strftime('%Y-%m-%d')
        # print(f"Processing {date_str}...") 
        
        combos = get_combinations(date_str)
        
        for combo in combos:
            combo_name = combo.get('name', 'Unknown')
            matches = combo.get('matches', [])
            
            if not matches:
                continue
                
            # Verify Combination Result
            all_won = True
            pending = False
            combined_odds = 1.0
            
            match_details = []
            
            for m in matches:
                market = m.get('market')
                prediction = m.get('prediction')
                
                # Correct keys are snake_case
                home_goals = m.get('actual_home_goals')
                away_goals = m.get('actual_away_goals')
                status = m.get('status') # FT, NS, etc.
                
                odds = m.get('odds', 0.0)
                # Normalize odds (API might return 162.00 or 1.62)
                if odds > 50: odds /= 100.0
                if odds < 1.01: odds = 1.0 # If 0 or missing, treat as 1.0 (Refund/No Profit)
                if odds > 50: odds /= 100.0
                if odds < 1.0: odds = 1.0
                
                combined_odds *= odds
                
                if status not in ['FT', 'AET', 'PEN']:
                    pending = True
                    break
                
                res = check_result(market, prediction, home_goals, away_goals)
                match_details.append(f"{market} ({res})")
                
                if res != "Win":
                    all_won = False
            
            if pending:
                continue

            stake = 100
            total_staked += stake
            total_combos += 1
            
            if all_won:
                total_won += 1
                payout = stake * combined_odds
                total_returned += payout
                profit = payout - stake
            else:
                profit = -stake
                if total_combos < 5: # Debug first 5 failures
                     print(f"[{date_str}] {combo_name}: LOST ({profit:.2f}) | Details: {', '.join(match_details)}")
            
            # Type Tracking
            if combo_name not in results_by_type:
                results_by_type[combo_name] = {'staked': 0, 'returned': 0, 'won': 0, 'total': 0}
            
            results_by_type[combo_name]['staked'] += stake
            results_by_type[combo_name]['returned'] += (payout if all_won else 0)
            results_by_type[combo_name]['total'] += 1
            if all_won: results_by_type[combo_name]['won'] += 1

        current_date += timedelta(days=1)

    print("-" * 60)
    print("BACKTEST SUMMARY (10 Weeks)")
    print("-" * 60)
    print(f"Total Combinations: {total_combos}")
    print(f"Total Won: {total_won} (Win Rate: {total_won/total_combos*100:.1f}%)" if total_combos > 0 else "Total Won: 0")
    print(f"Total Staked: {total_staked:.2f}")
    print(f"Total Returned: {total_returned:.2f}")
    
    net_profit = total_returned - total_staked
    roi = (net_profit / total_staked * 100) if total_staked > 0 else 0
    
    print(f"Net Profit: {net_profit:.2f}")
    print(f"ROI: {roi:.2f}%")
    print("-" * 60)
    print("BY COMBINATION TYPE:")
    for name, stats in results_by_type.items():
        s = stats['staked']
        r = stats['returned']
        p = r - s
        roi_type = (p / s * 100) if s > 0 else 0
        wr = (stats['won'] / stats['total'] * 100) if stats['total'] > 0 else 0
        print(f"{name}: ROI {roi_type:.1f}% | WR {wr:.1f}% | Profit {p:.0f}")

if __name__ == "__main__":
    run_backtest(weeks=10)
