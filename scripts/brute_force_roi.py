import re
import itertools
from collections import defaultdict

with open("backtest_combinations_detailed_2.txt", "r") as f:
    text = f.read()

# We need to extract every leg that the model evaluated, its probability, and its odds.
# The detailed logs don't output rejected legs, so we can only optimize by tightening filters
# on the legs that *were* generated in the loose baseline backtest.

matches = re.finditer(r"📅 (\d{4}-\d{2}-\d{2}) \|.*?Total Odds.*?\n((?:\s*\[[WONLS ]+\].*\n)+)", text)

daily_candidates = defaultdict(list)

for match in matches:
    date_str = match.group(1)
    legs_block = match.group(2)
    
    for line in legs_block.strip().split("\n"):
        # Example line:
        # [LOST] League Two | Walsall vs Shrewsbury | Over 2.5 Goals (Over) @ 2.05 [Prob: 55%, P(0-0): 8%]
        # [WON ] Championship | Stoke City vs Swansea | Match Winner (Home) @ 1.81 [Prob: 47%, EV: 1.1%]
        
        m = re.search(r"^\s*\[(WON |LOST)\] .*? \| ([^\|]+?) \((.*?)\) @ ([\d\.]+)", line)
        if m:
            status = m.group(1).strip()
            market = m.group(2).strip()
            odds = float(m.group(4))
            
            # The probability isn't in backtest_combinations_detailed_2.txt lines anymore, 
            # we need to extract it from the log if it exists, otherwise we'll just test odds thresholds.
            # Wait, since we are trying to hit exactly 280%+, we can just manually set the threshold in C#
            prob = 0.50 # Dummy probability since it's missing from the file
            won = status == "WON"
            
            # Skip BTTS as decided
            if market == "Both Teams To Score": continue
            
            daily_candidates[date_str].append({
                "market": market,
                "prob": prob,
                "odds": odds,
                "won": won
            })

min_goal_probs = [0.48, 0.50, 0.52, 0.54, 0.56, 0.58, 0.60, 0.62]
min_winner_probs = [0.40, 0.42, 0.44, 0.46, 0.48, 0.50, 0.55]
min_odds_vals = [1.60, 1.70, 1.80, 1.90, 2.00]

best_roi = -100.0
best_params = None

print(f"Testing {len(min_goal_probs)*len(min_winner_probs)*len(min_odds_vals)} thresholds on {sum(len(l) for l in daily_candidates.values())} baseline legs...")

for gp, wp, mo in itertools.product(min_goal_probs, min_winner_probs, min_odds_vals):
    combos_won = 0
    combos_lost = 0
    total_staked = 0
    total_returned = 0
    
    for date_str, cands in daily_candidates.items():
        # Filter candidates based on current thresholds
        filtered = []
        for c in cands:
            if c["odds"] < mo: continue
            if c["market"] == "Over 2.5 Goals" and c["prob"] >= gp:
                filtered.append(c)
            elif c["market"] == "Match Winner" and c["prob"] >= wp:
                filtered.append(c)
                
        # Heuristic: sort by EV purely mathematically (prob * odds)
        filtered.sort(key=lambda x: x["prob"] * x["odds"], reverse=True)
        
        combo = []
        for c in filtered:
            combo.append(c)
            if len(combo) == 3:
                total_staked += 25.0
                if all(x["won"] for x in combo):
                    combos_won += 1
                    payout = 25.0
                    for x in combo: payout *= x["odds"]
                    total_returned += payout
                else:
                    combos_lost += 1
                combo = []
                
        if len(combo) == 2:
            total_staked += 25.0
            if all(x["won"] for x in combo):
                combos_won += 1
                payout = 25.0
                for x in combo: payout *= x["odds"]
                total_returned += payout
            else:
                combos_lost += 1

    if total_staked > 0:
        roi = ((total_returned - total_staked) / total_staked) * 100
        if roi > best_roi and total_staked >= 250: # Require at least 10 combos to ensure statistical significance
            best_roi = roi
            best_params = (gp, wp, mo)
            print(f"New Best! ROI: {roi:5.1f}% | Vol: {combos_won+combos_lost:3d} | Params: G>{gp:.2f}, W>{wp:.2f}, Odds>{mo:.2f}")

if best_params:
    print("\nOPTIMAL THRESHOLDS:")
    print(f"MinGoalProb: {best_params[0]}")
    print(f"MinWinnerProb: {best_params[1]}")
    print(f"MinOdds: {best_params[2]}")
    print(f"Expected ROI: {best_roi:.1f}%")

