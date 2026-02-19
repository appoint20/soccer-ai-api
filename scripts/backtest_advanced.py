import urllib.request
import json
from datetime import datetime, timedelta

# Configuration
API_URL = "http://localhost:5165/api"
WEEKS_TO_BACKTEST = 15
DAYS_TO_BACKTEST = WEEKS_TO_BACKTEST * 7

def get_analysis(date_str):
    url = f"{API_URL}/analysis?date={date_str}&language=en"
    try:
        with urllib.request.urlopen(url) as response:
            if response.status == 200:
                data = json.loads(response.read().decode('utf-8'))
                return data.get('matches', [])
    except Exception as e:
        print(f"Error fetching {date_str}: {e}")
    return []

def run_backtest():
    end_date = datetime.now()
    start_date = end_date - timedelta(days=DAYS_TO_BACKTEST)
    current_date = start_date
    
    stats = {
        "total_analyzed": 0,
        "total_qualified": 0,
        "qualified_standard": 0,
        "qualified_alternative": 0,
        "excluded_bore_draw": 0,
        "wins": 0,
        "losses": 0,
        "pending": 0
    }
    
    # Failure Analysis Counters
    fail_stats = {
        "primary_step_1": 0,
        "primary_step_2": 0,
        "primary_step_3": 0,
        "primary_unknown": 0,
        "alt_overall_fail": 0,
        "alt_venue_fail": 0,
        "alt_h2h_fail": 0,
        "alt_multiple_fail": 0
    }
    
    # Debug counter
    debug_count = 0

    print(f"Running Advanced BTTS Backtest from {start_date.strftime('%Y-%m-%d')} to {end_date.strftime('%Y-%m-%d')}")
    print("=" * 60)
    
    while current_date <= end_date:
        date_str = current_date.strftime('%Y-%m-%d')
        matches = get_analysis(date_str)
        
        for m in matches:
            stats["total_analyzed"] += 1
            
            # Check Prediction/Decision
            prediction = m.get('prediction', {})
            if not prediction:
                continue
                
            btts = prediction.get('btts', {})
            reason = btts.get('reason', "")
            is_qualified = btts.get('is_qualified', False)
            
            # Outcomes
            result_obj = m.get('result')
            outcome = "UNKNOWN"
            if result_obj:
                 score = result_obj.get('actual_score')
                 if score and ":" in score:
                     parts = score.split(':')
                     hg = int(parts[0])
                     ag = int(parts[1])
                     if hg > 0 and ag > 0:
                         outcome = "WIN"
                     else:
                         outcome = "LOSS"
                 else:
                     outcome = "PENDING"
            else:
                 outcome = "PENDING"

            # DEBUG: Trace first 50 matches
            if stats["total_analyzed"] <= 50:
                 print(f"DEBUG MATCH {stats['total_analyzed']}: Qualified={is_qualified}, Reason={reason}")

            # Categorize
            if is_qualified:
                stats["total_qualified"] += 1
                if "Standard" in reason:
                    stats["qualified_standard"] += 1
                elif "Alternative" in reason:
                    stats["qualified_alternative"] += 1
                else:
                    if "unclassified" not in stats: stats["unclassified"] = 0
                    stats["unclassified"] += 1
                    print(f"Unclassified Success Reason: {reason}")
                
                if outcome == "WIN":
                    stats["wins"] += 1
                elif outcome == "LOSS":
                    stats["losses"] += 1
                else:
                    stats["pending"] += 1
            else:
                # Check for exclusions
                if "Bore Draw" in reason:
                    stats["excluded_bore_draw"] += 1
                elif "Failed Primary" in reason:
                     # DEBUG RAW STRING
                     # DEBUG RAW STRING
                     if debug_count < 10:
                         print(f"DEBUG RAW FAIL: {reason}")
                         debug_count += 1
                         
                     # Parse Primary Failure
                     if "Step 1 Failed" in reason:
                         fail_stats["primary_step_1"] += 1
                     elif "Step 2 Failed" in reason:
                         fail_stats["primary_step_2"] += 1
                     elif "Step 3 Failed" in reason:
                         fail_stats["primary_step_3"] += 1
                     else:
                         # Capture everything else
                         if debug_count < 20:
                             print(f"DEBUG UNKNOWN FAIL: {reason}")
                             debug_count += 1
                         
                     # Parse Alt Failure
                     # String format: "Failed Alt: Overall=x, Venue=y, H2H=z"
                     try:
                         if "Failed Alt:" in reason:
                             alt_part = reason.split("Failed Alt:")[1].strip()
                             # Overall=False, Venue=True, H2H=False
                             # We want to know which one caused the failure (False)
                             overall_ok = "Overall=True" in alt_part
                             venue_ok = "Venue=True" in alt_part
                             h2h_ok = "H2H=True" in alt_part
                             
                             failures = []
                             if not overall_ok: failures.append("Overall")
                             if not venue_ok: failures.append("Venue")
                             # Only check H2H if it's in the string (backwards compatibility)
                             if "H2H=" in alt_part and not h2h_ok: failures.append("H2H")
                             
                             if "Overall" in failures: fail_stats["alt_overall_fail"] += 1
                             if "Venue" in failures: fail_stats["alt_venue_fail"] += 1
                             if "H2H" in failures: fail_stats["alt_h2h_fail"] += 1
                         
                     except:
                         pass
        
        current_date += timedelta(days=1)
        print(f"Processed {date_str}...", end="\r")
        
    print("\n" + "=" * 60)
    print("BACKTEST RESULTS (10 WEEKS)")
    print("=" * 60)
    print(f"Total Matches Analyzed:       {stats['total_analyzed']}")
    print(f"Total BTTS Qualified:         {stats['total_qualified']} ({(stats['total_qualified']/stats['total_analyzed']*100 if stats['total_analyzed'] else 0):.2f}%)")
    print("-" * 30)
    print(f"  > Standard Filter:          {stats['qualified_standard']}")
    print(f"  > Alternative Filter:       {stats['qualified_alternative']}")
    print("-" * 30)
    print("EXCLUSION BREAKDOWN:")
    print(f"  > Bore Draw (Recent 0-0):   {stats['excluded_bore_draw']}")
    print(f"  > Logic Rejection:          {stats['total_analyzed'] - stats['total_qualified'] - stats['excluded_bore_draw']}")
    print("")
    print("PRIMARY FILTER FAILURES (Why Standard Failed):")
    print(f"  > Step 1 (Overall Range):   {fail_stats['primary_step_1']}")
    print(f"  > Step 2 (Venue Range):     {fail_stats['primary_step_2']}")
    print(f"  > Step 3 (H2H Low):         {fail_stats['primary_step_3']}")
    print("")
    print("ALTERNATIVE FILTER FAILURES:")
    print(f"  > Overall Stats Low:        {fail_stats['alt_overall_fail']}")
    print(f"  > Venue Stats Low:          {fail_stats['alt_venue_fail']}")
    print(f"  > H2H Stats Low (Legacy):   {fail_stats['alt_h2h_fail']}")
    print("-" * 30)
    
    completed = stats['wins'] + stats['losses']
    accuracy = (stats['wins'] / completed * 100) if completed > 0 else 0
    print(f"Accuracy (Completed):         {accuracy:.2f}% ({stats['wins']}/{completed})")
    print(f"Pending:                      {stats['pending']}")
    print("=" * 60)

if __name__ == "__main__":
    run_backtest()
