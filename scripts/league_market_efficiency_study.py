#!/usr/bin/env python3
import urllib.request
import json
import math
from datetime import datetime, timedelta

API_URL = "http://localhost:5165/api"
WEEKS_BACK = 20

def get_analysis_dates(weeks):
    today = datetime.now().date()
    # Start from last Sunday
    last_sunday = today - timedelta(days=(today.weekday() + 1) % 7)
    
    dates = []
    for w in range(weeks):
        sun = last_sunday - timedelta(weeks=w)
        sat = sun - timedelta(days=1)
        fri = sun - timedelta(days=2)
        dates.extend([fri, sat, sun])
    
    dates.sort()
    return dates

def fetch_analysis(date):
    url = f"{API_URL}/Analysis?date={date}"
    try:
        with urllib.request.urlopen(url, timeout=60) as resp:
            data = json.loads(resp.read().decode('utf-8'))
            return data.get('matches', [])
    except Exception as e:
        # print(f"Error fetching {date}: {e}")
        return []

def evaluate_market(market, pred_obj, hg, ag):
    if hg is None or ag is None: return None
    
    if market == 'Over 2.5':
        return (hg + ag) > 2.5
    elif market == 'BTTS':
        return hg > 0 and ag > 0
    elif market == '2-3 Goals':
        total = hg + ag
        return 2 <= total <= 3
    elif market == 'Winner':
        pred = pred_obj.get('prediction', '').lower()
        if pred == 'home': return hg > ag
        elif pred == 'away': return ag > hg
        elif pred == 'draw': return hg == ag
    return None

def main():
    print(f"Starting 20-Week League-Market Efficiency Study...")
    dates = get_analysis_dates(WEEKS_BACK)
    print(f"Analyzing {len(dates)} dates...")
    
    # Global stats: [market][league] = {wins, total, payout, stake}
    stats = {
        'Over 2.5': {},
        'BTTS': {},
        '2-3 Goals': {},
        'Winner': {}
    }

    for idx, d in enumerate(dates):
        if idx % 10 == 0: print(f"Processing batch {idx//10 + 1}...")
        matches = fetch_analysis(d)
        for m in matches:
            res = m.get('result')
            # In snake_case_lower, 'result' should have 'actual_score'
            if not res: continue
            
            score = res.get('actual_score')
            if not score or ':' not in score: continue
            
            try:
                hg, ag = map(int, score.split(':'))
            except: continue
            
            league = m.get('league', 'Unknown')
            preds = m.get('prediction')
            if not preds: continue

            # Markets to test
            # prediction object is like { "over25": { "is_qualified": true, ... }, ... }
            market_map = {
                'Over 2.5': (preds.get('over25'), m.get('odds_over25', 1.8)),
                'BTTS': (preds.get('btts'), m.get('odds_btts_yes', 1.8)),
                '2-3 Goals': (preds.get('two_to_three_goals'), 2.0), # Estimation
                'Winner': (preds.get('match_winner'), m.get('odds_home_win', 2.0))
            }

            for mkt_name, (pred_obj, odds) in market_map.items():
                if not pred_obj or not pred_obj.get('is_qualified'): continue
                
                win = evaluate_market(mkt_name, pred_obj, hg, ag)
                if win is None: continue
                
                if league not in stats[mkt_name]:
                    stats[mkt_name][league] = {'wins': 0, 'total': 0, 'payout': 0, 'stake': 0}
                
                stats[mkt_name][league]['total'] += 1
                stats[mkt_name][league]['stake'] += 1
                if win:
                    stats[mkt_name][league]['wins'] += 1
                    stats[mkt_name][league]['payout'] += (odds if odds > 1 else 1.8)

    # Report Generation
    print("\n" + "="*80)
    print("LEAGUE-MARKET EFFICIENCY REPORT (LAST 20 WEEKS)")
    print("="*80)

    for mkt_name, leagues in stats.items():
        print(f"\n📈 MARKET: {mkt_name}")
        print("-" * 60)
        print(f"{'League':<30} | {'Accuracy':<10} | {'ROI':<8} | {'Volume':<6}")
        
        # Sort by ROI
        sorted_leagues = []
        for l_name, s in leagues.items():
            acc = (s['wins'] / s['total'] * 100) if s['total'] > 0 else 0
            roi = ((s['payout'] - s['stake']) / s['stake'] * 100) if s['stake'] > 0 else 0
            sorted_leagues.append((l_name, acc, roi, s['total']))
        
        sorted_leagues.sort(key=lambda x: x[2], reverse=True)
        
        for l_name, acc, roi, vol in sorted_leagues:
            if vol < 5: continue # Filter low volume for meaningful trends
            print(f"{l_name[:30]:<30} | {acc:>8.1f}% | {roi:>+7.1f}% | {vol:>6}")

if __name__ == "__main__":
    main()
