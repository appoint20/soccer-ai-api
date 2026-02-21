#!/usr/bin/env python3
"""
Deep Analysis — drills into specific matches to understand why predictions failed.
Queries the /api/analysis endpoint for each match and shows all model outputs.
"""

import urllib.request
import json
from datetime import datetime

API_URL = "http://localhost:5165/api"

def fetch_analysis(date):
    url = f"{API_URL}/analysis?date={date}"
    try:
        with urllib.request.urlopen(url, timeout=120) as resp:
            return json.loads(resp.read().decode('utf-8'))
    except Exception as e:
        print(f"  ⚠ Error fetching {url}: {e}")
        return None

def fetch_combinations(date):
    url = f"{API_URL}/combinations?date={date}"
    try:
        with urllib.request.urlopen(url, timeout=120) as resp:
            data = json.loads(resp.read().decode('utf-8'))
            return data.get('combinations', [])
    except Exception as e:
        print(f"  ⚠ Error fetching combos: {e}")
        return []

def print_separator(char='═', width=80):
    print(char * width)

def analyze_match(match_data):
    """Print deep analysis of a single match."""
    home = match_data.get('home_team', 'Unknown')
    away = match_data.get('away_team', 'Unknown')
    league = match_data.get('league', 'Unknown')
    
    hg = match_data.get('home_goals', '?')
    ag = match_data.get('away_goals', '?')
    status = match_data.get('status', '?')
    
    print(f"\n{'─'*80}")
    print(f"⚽ {home} vs {away}")
    print(f"   League: {league}")
    print(f"   Score: {hg} - {ag} ({status})")
    print(f"{'─'*80}")
    
    # Team Stats
    ts = match_data.get('team_stats', {})
    home_stats = ts.get('home', {})
    away_stats = ts.get('away', {})
    
    print(f"\n📊 TEAM STATS:")
    print(f"   {'Metric':<25s}  {'Home':>8s}  {'Away':>8s}")
    print(f"   {'─'*45}")
    
    stat_fields = [
        ('avg_goals_scored_last7', 'AvgGoals Scored (L7)'),
        ('avg_goals_conceded_last7', 'AvgGoals Conceded (L7)'),
        ('over25_rate', 'Over 2.5 Rate'),
        ('btts_rate', 'BTTS Rate'),
        ('clean_sheet_rate', 'Clean Sheet Rate'),
        ('win_rate', 'Win Rate'),
        ('form_percentage', 'Form %'),
    ]
    
    for field, label in stat_fields:
        hv = home_stats.get(field, 'N/A')
        av = away_stats.get(field, 'N/A')
        if isinstance(hv, float): hv = f"{hv:.2f}"
        if isinstance(av, float): av = f"{av:.2f}"
        print(f"   {label:<25s}  {str(hv):>8s}  {str(av):>8s}")
    
    # H2H
    h2h = match_data.get('h2h', {})
    if h2h:
        print(f"\n🔄 HEAD-TO-HEAD:")
        print(f"   Total Matches: {h2h.get('total_matches', '?')}")
        print(f"   Avg Goals: {h2h.get('avg_goals', '?')}")
        print(f"   Over 2.5 Rate: {h2h.get('over25_rate', '?')}")
        print(f"   BTTS Rate: {h2h.get('btts_rate', '?')}")
        print(f"   Home Win Rate: {h2h.get('home_win_rate', '?')}")
        print(f"   Draw Rate: {h2h.get('draw_rate', '?')}")
    
    # Statistical Models
    stats = match_data.get('statistical_models', {})
    poisson = stats.get('poisson', {})
    mc = stats.get('monte_carlo', {})
    
    if poisson:
        print(f"\n📐 POISSON MODEL:")
        print(f"   Expected Home Goals: {poisson.get('expected_home_goals', '?')}")
        print(f"   Expected Away Goals: {poisson.get('expected_away_goals', '?')}")
        print(f"   Over 2.5: {poisson.get('over25', '?')}")
        print(f"   BTTS:     {poisson.get('btts', '?')}")
        print(f"   Home Win: {poisson.get('home_win', '?')}")
        print(f"   Draw:     {poisson.get('draw', '?')}")
        print(f"   Away Win: {poisson.get('away_win', '?')}")
    
    if mc:
        print(f"\n🎲 MONTE CARLO MODEL:")
        print(f"   Over 2.5: {mc.get('over25', '?')}")
        print(f"   BTTS:     {mc.get('btts', '?')}")
        print(f"   Home Win: {mc.get('home_win', '?')}")
        print(f"   Draw:     {mc.get('draw', '?')}")
        print(f"   Away Win: {mc.get('away_win', '?')}")
    
    # ML Prediction
    ml = match_data.get('ml_prediction', {})
    if ml:
        print(f"\n🤖 ML PREDICTION:")
        over25 = ml.get('over25', {})
        btts = ml.get('btts', {})
        hda = ml.get('hda', {})
        print(f"   Over 2.5: pred={over25.get('prediction', '?')} conf={over25.get('confidence', '?')} probs={over25.get('probabilities', '?')}")
        print(f"   BTTS:     pred={btts.get('prediction', '?')} conf={btts.get('confidence', '?')} probs={btts.get('probabilities', '?')}")
        print(f"   HDA:      pred={hda.get('prediction', '?')} conf={hda.get('confidence', '?')} probs={hda.get('probabilities', '?')}")
    
    # Weighted Prediction (Consensus)
    wp = match_data.get('weighted_prediction', {})
    if wp:
        print(f"\n⚖️ WEIGHTED CONSENSUS:")
        print(f"   Over 2.5: {wp.get('over25', '?')} (prob={wp.get('over25_prob', '?')})")
        print(f"   BTTS:     {wp.get('btts', '?')} (prob={wp.get('btts_prob', '?')})")
        print(f"   Winner:   {wp.get('match_winner', '?')} (conf={wp.get('confidence', '?')})")
    
    # Decisions
    decisions = match_data.get('decisions', {})
    markets = decisions.get('markets', {})
    trap = decisions.get('trap', {})
    qual = decisions.get('qualification', {})
    decision_tier = decisions.get('decision', '?')
    
    print(f"\n🎯 DECISION SERVICE:")
    print(f"   Overall: {qual.get('label', '?')} | Decision Tier: {decision_tier}")
    print(f"   Trap: {trap.get('is_trap', False)} ({trap.get('reason', '')})")
    
    for mkt_name in ['over25', 'btts', 'match_winner', 'low_scoring', 'draw']:
        mkt = markets.get(mkt_name, {})
        if mkt:
            print(f"   {mkt_name.upper():<15s}: qualified={mkt.get('is_qualified', '?')} conf={mkt.get('confidence', '?')} reason={mkt.get('reason', '')}")
    
    # Odds
    odds = match_data.get('odds', {})
    if odds:
        print(f"\n💰 ODDS:")
        print(f"   Over 2.5: {odds.get('over25', '?')}  Under 2.5: {odds.get('under25', '?')}")
        print(f"   BTTS Yes: {odds.get('btts_yes', '?')}")
        print(f"   Home: {odds.get('home_win', '?')}  Draw: {odds.get('draw', '?')}  Away: {odds.get('away_win', '?')}")

def main():
    # ══════════════════════════════════════════════════════════════
    # PART 1: Feb 14 Combinations — All 4 combos failed
    # ══════════════════════════════════════════════════════════════
    print_separator()
    print("PART 1: DEEP ANALYSIS — Feb 14, 2026 (All 4 Combos Lost)")
    print_separator()
    
    # Fetch analysis for Feb 14
    print("\nFetching full analysis for 2026-02-14...")
    data = fetch_analysis("2026-02-14")
    
    if data:
        matches = data if isinstance(data, list) else data.get('matches', data.get('analysis', []))
        if isinstance(matches, dict):
            matches = list(matches.values()) if matches else []
        
        # Find the specific failed matches
        failed_teams = [
            ("Bayer Leverkusen", "FC St. Pauli"),         # BTTS LOST
            ("Cultural Leonesa", "Zaragoza"),              # BTTS LOST
            ("Paris FC", "Lens"),                          # BTTS LOST
            ("QPR", "Blackburn"),                          # Winner LOST
            ("Exeter City", "Northampton"),                # Winner LOST
            # Also show the winners for comparison
            ("Lille", "Stade Brestois"),                   # BTTS WON
            ("Granada", "Valladolid"),                     # BTTS WON
            ("Sevilla", "Alaves"),                         # BTTS WON
            ("Hoffenheim", "Freiburg"),                    # Winner WON
            ("Barnsley", "AFC Wimbledon"),                 # Over 2.5 WON
        ]
        
        found = set()
        for m in matches:
            home = m.get('home_team', '')
            away = m.get('away_team', '')
            
            for ft in failed_teams:
                if (ft[0].lower() in home.lower() or ft[0].lower() in away.lower()) and \
                   (ft[1].lower() in home.lower() or ft[1].lower() in away.lower()):
                    if ft not in found:
                        found.add(ft)
                        analyze_match(m)
        
        not_found = [ft for ft in failed_teams if ft not in found]
        if not_found:
            print(f"\n⚠ Could not find these matches in analysis: {not_found}")
        
        print(f"\n📋 Total matches analyzed on this date: {len(matches)}")
    else:
        print("  ❌ Could not fetch analysis data")
    
    # ══════════════════════════════════════════════════════════════
    # PART 2: Preston vs Watford — Feb 17 (Missed BTTS/Over 2.5)
    # ══════════════════════════════════════════════════════════════
    print("\n")
    print_separator()
    print("PART 2: DEEP ANALYSIS — Preston vs Watford (Feb 17, 2026)")
    print("Actual Score: 2-2 | H2H says BTTS+Over 2.5 but model said NoBet")
    print_separator()
    
    print("\nFetching full analysis for 2026-02-17...")
    data = fetch_analysis("2026-02-17")
    
    if data:
        matches = data if isinstance(data, list) else data.get('matches', data.get('analysis', []))
        if isinstance(matches, dict):
            matches = list(matches.values()) if matches else []
        
        found_preston = False
        for m in matches:
            home = m.get('home_team', '')
            away = m.get('away_team', '')
            if 'preston' in home.lower() or 'preston' in away.lower() or \
               'watford' in home.lower() or 'watford' in away.lower():
                found_preston = True
                analyze_match(m)
        
        if not found_preston:
            print("  ⚠ Preston vs Watford not found in analysis response")
            print(f"  Available matches ({len(matches)}):")
            for m in matches:
                print(f"    - {m.get('home_team', '?')} vs {m.get('away_team', '?')} ({m.get('league', '?')})")
    else:
        print("  ❌ Could not fetch analysis data for Feb 17")

if __name__ == "__main__":
    main()
