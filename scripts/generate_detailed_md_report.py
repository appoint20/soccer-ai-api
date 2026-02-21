#!/usr/bin/env python3
import urllib.request
import json
from datetime import datetime, timedelta

API_URL = "http://localhost:5165/api"
WEEKS_BACK = 10

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
        with urllib.request.urlopen(url, timeout=60) as resp:
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
    if market == 'Over 2.5 Goals': return (hg + ag) > 2.5
    elif market == 'Under 2.5 Goals': return (hg + ag) < 2.5
    elif market == 'Both Teams To Score': return hg > 0 and ag > 0
    elif market == 'Match Winner':
        if prediction.lower() == 'home': return hg > ag
        elif prediction.lower() == 'away': return ag > hg
        elif prediction.lower() == 'draw': return hg == ag
    return None

def main():
    dates = get_weekend_dates(WEEKS_BACK)
    
    total_staked = 0
    total_returned = 0
    stake = 25.0
    
    # Analytics tracking
    # league_stats[market][league] = {'wins': 0, 'total': 0}
    league_stats = {}
    
    daily_sections = ""

    for d in dates:
        combos = fetch_combinations(d)
        if not combos: continue
        
        daily_sections += f"## 📅 Date: {d}\n\n"
        
        for combo in combos:
            name = combo.get('name', 'Combination')
            matches = combo.get('matches', [])
            if not matches: continue
            
            combo_odds = 1.0
            results = []
            
            daily_sections += f"### {name}\n"
            daily_sections += "| Time | League | Match | Market | Selection | Odds | Result |\n"
            daily_sections += "| :--- | :--- | :--- | :--- | :--- | :--- | :--- |\n"
            
            for m in matches:
                time_str = m.get('match_date', '00:00').split('T')[-1][:5]
                league = m.get('league_name', 'Unknown')
                match_text = f"{m.get('home_team')} vs {m.get('away_team')}"
                market = m.get('market')
                selection = m.get('prediction')
                odds = m.get('odds', 1.0)
                if odds > 50: odds /= 100.0
                
                res = evaluate_leg(m)
                res_icon = "⏳ PENDING" if res is None else ("✅ WON" if res else "❌ LOST")
                results.append(res)
                
                # Update analytics
                if res is not None:
                    if market not in league_stats: league_stats[market] = {}
                    if league not in league_stats[market]: league_stats[market][league] = {'wins': 0, 'total': 0}
                    league_stats[market][league]['total'] += 1
                    if res: league_stats[market][league]['wins'] += 1
                
                daily_sections += f"| {time_str} | {league} | {match_text} | {market} | {selection} | {odds:.2f} | {res_icon} |\n"
                combo_odds *= odds

            total_staked += stake
            if all(r is True for r in results):
                win = stake * combo_odds
                total_returned += win
                status = f"🟢 **WON (+€{win-stake:.2f})**"
            elif any(r is False for r in results):
                status = f"🔴 **LOST (-€{stake:.2f})**"
            else:
                status = "⏳ **PENDING**"
            
            daily_sections += f"\n**Combo Status**: {status} | **Total Odds**: {combo_odds:.2f}\n\n---\n\n"

    # Generate Performance Analytics Section
    analytics_md = "## 🏆 League Performance Analytics (by Market)\n\n"
    analytics_md += "This section identifies which leagues are the most reliable for specific betting markets.\n\n"
    
    for market, leagues in sorted(league_stats.items()):
        analytics_md += f"### 📌 Market: {market}\n"
        analytics_md += "| League | Accuracy | Record (W-L) |\n"
        analytics_md += "| :--- | :--- | :--- |\n"
        
        # Sort leagues by accuracy
        sorted_leagues = []
        for l_name, stats in leagues.items():
            acc = (stats['wins'] / stats['total'] * 100) if stats['total'] > 0 else 0
            sorted_leagues.append((l_name, acc, stats['wins'], stats['total'] - stats['wins']))
        
        sorted_leagues.sort(key=lambda x: x[1], reverse=True)
        
        for l_name, acc, wins, losses in sorted_leagues:
            status_icon = "🔥" if acc >= 75 else ("⚠️" if acc <= 40 else "📊")
            analytics_md += f"| {status_icon} {l_name} | **{acc:.1f}%** | {wins}-{losses} |\n"
        analytics_md += "\n"

    roi = ((total_returned - total_staked) / total_staked * 100) if total_staked > 0 else 0
    summary = f"# 🏟️ Exhaustive Historical Combinations Report\n\n"
    summary += "## 📈 Final Summary\n"
    summary += f"- **Total Staked**: €{total_staked:.2f}\n"
    summary += f"- **Total Returned**: €{total_returned:.2f}\n"
    summary += f"- **Net Profit**: €{total_returned - total_staked:.2f}\n"
    summary += f"- **Overall ROI**: {roi:+.1f}%\n\n"
    
    final_report = summary + analytics_md + daily_sections
    with open("detailed_historical_report.md", "w") as f:
        f.write(final_report)

if __name__ == "__main__":
    main()
