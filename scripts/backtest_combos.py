#!/usr/bin/env python3
"""
Combination ROI Backtest — tests the /api/combinations endpoint over 10 weeks.
Evaluates each combo as a parlay: all legs must win for the combo to pay out.
Tracks ROI, win rate, and per-market breakdown.
"""

import urllib.request
import json
import math
from datetime import datetime, timedelta

API_URL = "http://localhost:5165/api"
WEEKS_BACK = 10
STAKE = 25.0  # €25 per combo

def get_weekend_dates(weeks):
    """Get Saturday + Sunday for last N weeks."""
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
        with urllib.request.urlopen(url, timeout=60) as resp:
            data = json.loads(resp.read().decode('utf-8'))
            return data.get('combinations', [])
    except Exception as e:
        return []

def evaluate_leg(match):
    """Check if a single leg won."""
    hg = match.get('actual_home_goals')
    ag = match.get('actual_away_goals')
    status = match.get('status')
    
    if hg is None or ag is None or status != 'FT':
        return None  # Not finished
    
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
    print(f"Starting Combination ROI Backtest ({WEEKS_BACK} weeks)")
    print(f"Stake: €{STAKE:.2f} per combo")
    print("=" * 70)
    
    dates = get_weekend_dates(WEEKS_BACK)
    
    all_combos = []
    total_staked = 0.0
    total_returned = 0.0
    
    combo_wins = 0
    combo_losses = 0
    combo_pending = 0
    
    leg_wins = 0
    leg_losses = 0
    leg_total = 0
    
    market_stats = {}  # market -> {wins, losses}
    daily_results = []  # (date, combos, wins, staked, returned)
    
    for d in dates:
        combos = fetch_combinations(d)
        day_combos = 0
        day_wins = 0
        day_staked = 0.0
        day_returned = 0.0
        for combo in combos:
            matches = combo.get('matches', [])
            combo_name = combo.get('name', 'Combo')
            if not matches:
                continue
            
            leg_results = []
            combo_odds = 1.0
            has_odds = True
            leg_details = []
            
            for match in matches:
                result = evaluate_leg(match)
                odds = match.get('odds', 0)
                
                if odds > 50:
                    odds = odds / 100.0
                
                res_str = "PEND" if result is None else ("WON " if result else "LOST")
                leg_details.append(f"    [{res_str}] {match.get('league_name')} | {match.get('home_team')} vs {match.get('away_team')} | {match.get('market')} ({match.get('prediction')}) @ {odds:.2f}")

                if result is None:
                    leg_results.append(None)
                else:
                    leg_results.append(result)
                    leg_total += 1
                    
                    mkt = match.get('market', 'Unknown')
                    if mkt not in market_stats:
                        market_stats[mkt] = {'wins': 0, 'losses': 0}
                    
                    if result:
                        leg_wins += 1
                        market_stats[mkt]['wins'] += 1
                    else:
                        leg_losses += 1
                        market_stats[mkt]['losses'] += 1
                
                if odds > 1:
                    combo_odds *= odds
                else:
                    has_odds = False
            
            if None in leg_results:
                combo_pending += 1
                continue
            
            day_combos += 1
            day_staked += STAKE
            total_staked += STAKE
            
            all_won = all(leg_results)
            payout = STAKE * combo_odds if all_won and has_odds else 0.0
            
            pl_str = f"+€{(payout-STAKE):.2f}" if all_won else f"-€{STAKE:.2f}"
            status_str = "🟢 WON " if all_won else "🔴 LOST"
            print(f"\n📅 {d} | {combo_name} | Total Odds: {combo_odds:.2f} | Stake: €{STAKE:.2f} | {status_str} ({pl_str})")
            for ld in leg_details:
                print(ld)
                
            if all_won and has_odds:
                total_returned += payout
                day_returned += payout
                combo_wins += 1
                day_wins += 1
            else:
                combo_losses += 1
        
        if day_combos > 0:
            day_roi = ((day_returned - day_staked) / day_staked * 100) if day_staked > 0 else 0
            daily_results.append((d, day_combos, day_wins, day_staked, day_returned))
    
    # ── Summary ──
    settled = combo_wins + combo_losses
    print("\n" + "=" * 70)
    print("COMBINATION ROI BACKTEST RESULTS")
    print(f"(Poisson 35% + MC 40% + ML 15% + Market 10% + EV filter)")
    print("=" * 70)
    
    print(f"\n  Total Staked:    €{total_staked:.2f}")
    print(f"  Total Returned:  €{total_returned:.2f}")
    profit = total_returned - total_staked
    roi = (profit / total_staked * 100) if total_staked > 0 else 0
    print(f"  Profit/Loss:     €{profit:+.2f}")
    print(f"  ROI:             {roi:+.1f}%")
    
    print(f"\n  Combos Settled:  {settled}")
    print(f"  Combos Won:      {combo_wins}")
    print(f"  Combos Lost:     {combo_losses}")
    print(f"  Combo Win Rate:  {combo_wins/settled*100:.1f}%" if settled > 0 else "  No data")
    print(f"  Combos Pending:  {combo_pending}")
    
    print(f"\n  Individual Legs: {leg_total}")
    print(f"  Legs Won:        {leg_wins}")
    print(f"  Legs Lost:       {leg_losses}")
    print(f"  Leg Hit Rate:    {leg_wins/leg_total*100:.1f}%" if leg_total > 0 else "  No data")
    
    # ── Per-market breakdown ──
    print(f"\n{'─'*70}")
    print("PER-MARKET LEG ACCURACY:")
    print(f"{'─'*70}")
    for mkt, stats in sorted(market_stats.items()):
        total = stats['wins'] + stats['losses']
        acc = stats['wins'] / total * 100 if total > 0 else 0
        print(f"  {mkt:<20s}  {stats['wins']:>3}/{total:<3}  ({acc:.1f}%)")
    
    # ── Daily P&L ──
    if daily_results:
        print(f"\n{'─'*70}")
        print("DAILY P&L:")
        print(f"{'─'*70}")
        print(f"  {'Date':<12s}  {'Combos':>6s}  {'Won':>4s}  {'Staked':>8s}  {'Return':>8s}  {'P&L':>8s}  {'ROI':>6s}")
        running_pl = 0
        for date, combos, wins, staked, returned in daily_results:
            pl = returned - staked
            running_pl += pl
            day_roi = pl / staked * 100 if staked > 0 else 0
            print(f"  {date}  {combos:>6}  {wins:>4}  €{staked:>7.2f}  €{returned:>7.2f}  €{pl:>+7.2f}  {day_roi:>+5.1f}%")
        print(f"  {'':─<12}  {'':─>6}  {'':─>4}  {'':─>8}  {'':─>8}  {'':─>8}")
        print(f"  {'TOTAL':<12s}  {settled:>6}  {combo_wins:>4}  €{total_staked:>7.2f}  €{total_returned:>7.2f}  €{profit:>+7.2f}  {roi:>+5.1f}%")

if __name__ == "__main__":
    main()
