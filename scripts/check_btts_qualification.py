import urllib.request
import json
from datetime import datetime, timedelta

def get_analysis(date_str):
    # Use 'force_refresh=true' if consistent with API, but usually standard is fine.
    url = f"http://localhost:5165/api/analysis?date={date_str}&language=en"
    try:
        with urllib.request.urlopen(url) as response:
            if response.status == 200:
                data = json.loads(response.read().decode('utf-8'))
                return data.get('matches', [])
    except Exception as e:
        print(f"Error fetching {date_str}: {e}")
    return []

def debug_specific_match():
    date_str = "2026-02-08"
    print(f"DEBUG: Fetching {date_str} to find Bayern vs Hoffenheim")
    matches = get_analysis(date_str)
    
    print(f"DEBUG: Found {len(matches)} matches.")
    if len(matches) > 0:
        print(f"DEBUG: Match keys: {matches[0].keys()}")
        if 'fixture' in matches[0]:
            print(f"DEBUG: Fixture keys: {matches[0]['fixture'].keys()}")
            
    for m in matches:
        home = m.get('home_team')
        away = m.get('away_team')
        print(f"Match: {home} vs {away}")
        
        if home and "Bayern" in home or away and "Hoffenheim" in away:
            print(f"FOUND MATCH: {home} vs {away}")
            decision = m.get('decision', {})
            btts = decision.get('markets', {}).get('btts', {})
            print(f"Decision Keys: {decision.keys()}")
            print(f"Markets Keys: {decision.get('markets', {}).keys()}")
            print(f"BTTS Object: {json.dumps(btts, indent=2)}")
            return

def run_check(days=14):
    end_date = datetime.now()
    start_date = end_date - timedelta(days=days)
    current_date = start_date
    
    total_matches = 0
    btts_qualified = 0
    btts_wins = 0
    btts_qualified_matches = []
    
    print(f"Checking BTTS Qualification from {start_date.strftime('%Y-%m-%d')} to {end_date.strftime('%Y-%m-%d')}")
    print("=" * 60)
    
    while current_date <= end_date:
        date_str = current_date.strftime('%Y-%m-%d')
        analysis_list = get_analysis(date_str)
        
        day_total = 0
        day_qualified = 0
        
        for match in analysis_list:
            day_total += 1
            
            # Check if BTTS qualified
            # Structure: match['prediction']['btts']['is_qualified']
            try:
                prediction = match.get('prediction', {})
                if not prediction:
                    continue
                    
                btts = prediction.get('btts', {})
                
                # Check qualification
                if btts.get('is_qualified', False):
                    day_qualified += 1
                    btts_qualified += 1
                    
                    # Check result
                    # result key: match['result']['actual_score'] or similar?
                    # Handler: Result = matchResult
                    # matchResult has { ActualScore, IsCorrect }
                    # It doesn't have raw goals?
                    # Wait, handler says: ActualScore = $"{fixture.HomeGoal}:{fixture.AwayGoal}"
                    # We might need to parse it.
                    
                    result_obj = match.get('result')
                    status = "Pending"
                    
                    if result_obj:
                         score = result_obj.get('actual_score')
                         if score and ":" in score:
                             parts = score.split(':')
                             hg = int(parts[0])
                             ag = int(parts[1])
                             if hg > 0 and ag > 0:
                                 status = "WIN"
                                 btts_wins += 1
                             else:
                                 status = "LOSS"
                    
                    home_team = match.get('home_team')
                    away_team = match.get('away_team')
                    conf = btts.get('probability', 0) # Handler maps Probability to this field
                    
                    btts_qualified_matches.append(f"[{date_str}] {home_team} vs {away_team} (Conf: {conf:.2f}) -> {status}")
            except Exception as e:
                # print(f"Error parsing match: {e}")
                pass
                
        # print(f"{date_str}: {day_qualified}/{day_total} Qualified")
        print(f"DEBUG: {date_str} - Found {len(analysis_list)} matches.")
        if len(analysis_list) > 0 and day_total == 0:
             # Check first match structure
             print(f"DEBUG: First match keys: {analysis_list[0].keys()}")
             if 'decision' in analysis_list[0]:
                 print(f"DEBUG: decision keys: {analysis_list[0]['decision'].keys()}")
                 if 'markets' in analysis_list[0]['decision']:
                     print(f"DEBUG: markets keys: {analysis_list[0]['decision']['markets'].keys()}")
                     print(f"DEBUG: BTTS val: {analysis_list[0]['decision']['markets'].get('btts')}")
        
        total_matches += day_total
        current_date += timedelta(days=1)
        
    print("-" * 60)
    print(f"Total Matches Analyzed: {total_matches}")
    print(f"BTTS Qualified: {btts_qualified}")
    
    rate = (btts_qualified / total_matches * 100) if total_matches > 0 else 0
    print(f"Qualification Rate: {rate:.2f}%")
    
    # Calculate accuracy of qualified
    # Note: Pending matches are not losses, but we need to know how many finished.
    # Simple check: separate finished matches?
    # For now, just show wins.
    print(f"BTTS Wins (known): {btts_wins}")
    
    print("-" * 60)
    print("Qualified Matches Sample:")
    for m in btts_qualified_matches[:20]:
        print(m)
    if len(btts_qualified_matches) > 20:
        print(f"... and {len(btts_qualified_matches) - 20} more.")

if __name__ == "__main__":
    # debug_specific_match()
    run_check(days=14)
