#!/usr/bin/env python3
"""
Generates JSON Backtest data for the frontend.
"""

import urllib.request
import json
import math
from datetime import datetime, timedelta
import os

API_URL = "http://localhost:5165/api"
WEEKS_BACK = 10
STAKE = 1.0  # Base unit for calculation

def get_weekend_dates(weeks):
    today = datetime.now().date()
    current_saturday = today - timedelta(days=(today.weekday() + 2) % 7)
    dates = []
    for w in range(weeks):
        sat = current_saturday - timedelta(weeks=w)
        sun = sat + timedelta(days=1)
        dates.extend([sat, sun])
    dates.sort()
    return dates

def fetch_combinations(date):
    url = f"{API_URL}/combinations?date={date}"
    try:
        req = urllib.request.Request(url, headers={'Bypass-Tunnel-Reminder': 'true'})
        with urllib.request.urlopen(req, timeout=60) as resp:
            data = json.loads(resp.read().decode('utf-8'))
            return data.get('combinations', [])
    except Exception as e:
        return []

def evaluate_leg(match):
    hg = match.get('actual_home_goals')
    ag = match.get('actual_away_goals')
    status = match.get('status')
    
    if hg is None or ag is None or status != 'FT':
        return None
    
    market = match.get('market', '')
    prediction = match.get('prediction', '')
    
    if market == 'Over 2.5 Goals':
        return (hg + ag) > 2.5
    elif market == 'Under 2.5 Goals':
        return (hg + ag) < 2.5
    elif market == 'Both Teams To Score':
        return hg > 0 and ag > 0
    elif market == '2-3 Goals':
        total = hg + ag
        return 2 <= total <= 3
    elif market == 'Match Winner':
        if prediction.lower() == 'home':
            return hg > ag
        elif prediction.lower() == 'away':
            return ag > hg
        elif prediction.lower() == 'draw':
            return hg == ag
    return None

def main():
    dates = get_weekend_dates(WEEKS_BACK)
    
    total_staked = 0.0
    total_returned = 0.0
    combo_wins = 0
    combo_losses = 0
    leg_wins = 0
    leg_total = 0
    
    market_stats = {}
    league_stats = {}
    daily_results = []
    
    print(f"Fetching data for {len(dates)} dates...")

    for d in dates:
        combos = fetch_combinations(d)
        day_staked = 0.0
        day_returned = 0.0
        
        for combo in combos:
            matches = combo.get('matches', [])
            if not matches: continue
            
            leg_results = []
            combo_odds = 1.0
            has_odds = True
            
            for match in matches:
                result = evaluate_leg(match)
                odds = match.get('odds', 0)
                if odds > 50: odds = odds / 100.0
                
                if result is not None:
                    leg_results.append(result)
                    leg_total += 1
                    mkt = match.get('market', 'Unknown')
                    league = match.get('league_name', 'Unknown')
                    
                    if mkt not in market_stats:
                        market_stats[mkt] = {'wins': 0, 'losses': 0}
                    if league not in league_stats:
                        league_stats[league] = {}
                    if mkt not in league_stats[league]:
                        league_stats[league][mkt] = {'wins': 0, 'losses': 0}
                        
                    if result:
                        leg_wins += 1
                        market_stats[mkt]['wins'] += 1
                        league_stats[league][mkt]['wins'] += 1
                    else:
                        market_stats[mkt]['losses'] += 1
                        league_stats[league][mkt]['losses'] += 1
                else:
                    leg_results.append(None)
                
                if odds > 1: combo_odds *= odds
                else: has_odds = False
            
            if None in leg_results: continue
            
            day_staked += STAKE
            total_staked += STAKE
            
            all_won = all(leg_results)
            payout = STAKE * combo_odds if all_won and has_odds else 0.0
            
            if all_won and has_odds:
                total_returned += payout
                day_returned += payout
                combo_wins += 1
            else:
                combo_losses += 1
                
        if day_staked > 0:
            daily_results.append({
                "date": str(d),
                "staked": round(day_staked, 2),
                "returned": round(day_returned, 2),
                "pl": round(day_returned - day_staked, 2),
                "roi": round(((day_returned - day_staked) / day_staked) * 100, 2)
            })

    output = {
        "summary": {
            "total_staked_units": round(total_staked, 2),
            "total_returned_units": round(total_returned, 2),
            "pl_units": round(total_returned - total_staked, 2),
            "roi_percent": round(((total_returned - total_staked) / total_staked * 100), 2) if total_staked > 0 else 0,
            "combos_won": combo_wins,
            "combos_total": combo_wins + combo_losses,
            "win_rate": round(combo_wins / (combo_wins + combo_losses) * 100, 2) if (combo_wins + combo_losses) > 0 else 0,
            "leg_hit_rate": round(leg_wins / leg_total * 100, 2) if leg_total > 0 else 0
        },
        "markets": [],
        "daily": daily_results
    }

    for m, s in market_stats.items():
        t = s['wins'] + s['losses']
        output["markets"].append({
            "market": m,
            "wins": s['wins'],
            "total": t,
            "accuracy": round(s['wins'] / t * 100, 2) if t > 0 else 0
        })

    output["leagues"] = []
    for l, mkts in league_stats.items():
        for m, s in mkts.items():
            t = s['wins'] + s['losses']
            output["leagues"].append({
                "league": l,
                "market": m,
                "wins": s['wins'],
                "total": t,
                "accuracy": round(s['wins'] / t * 100, 2) if t > 0 else 0
            })
    
    # Sort leagues by total volume then accuracy descending
    output["leagues"].sort(key=lambda x: (x["total"], x["accuracy"]), reverse=True)

    # Save to frontend directory
    out_path = "/Users/shivm/Workspace/soccer-gpt-frontend/backtest_data.json"
    with open(out_path, "w") as f:
        json.dump(output, f, indent=2)
        
    print(f"Data saved to {out_path}")

if __name__ == "__main__":
    main()
