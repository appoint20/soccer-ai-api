
import requests
import json
from datetime import datetime, timedelta

# Configuration
API_URL = "http://localhost:5166/api/combinations"
WEEKS_BACK = 10

def get_weekend_dates(weeks_back=10):
    dates = []
    today = datetime.now()
    idx = (today.weekday() + 2) % 7
    saturday = today - timedelta(days=idx)
    current = saturday - timedelta(weeks=1) 
    
    for _ in range(weeks_back):
        dates.append(current.strftime('%Y-%m-%d'))
        dates.append((current + timedelta(days=1)).strftime('%Y-%m-%d'))
        current -= timedelta(weeks=1)
        
    return sorted(dates)

def run_funnel_analysis():
    dates = get_weekend_dates(WEEKS_BACK)
    print(f"Starting Funnel Analysis for last {WEEKS_BACK} weeks...")
    
    total_matches_in_db = 0 # Estimated
    total_ml_qualified = 0 # Candidates returned by API before filter (we can infer this if API exposes candidates or debug log)
    total_filter_qualified = 0 # Matches actually in combinations
    
    # Note: Since the API currently only returns the FINAL combinations, we can only accurately count the final qualified matches.
    # To get "Total Matches" and "ML Qualified", we would ideally need a "debug" mode in the API.
    # However, we can approximate "Total Matches" by assuming ~50 matches per weekend day for 12 leagues.
    # We will count "Filter Qualified" exactly from the response.
    
    total_combinations = 0
    total_legs = 0
    date_count = 0
    
    for date_str in dates:
        date_count += 1
        # print(f"Processing {date_str}...", end=" ", flush=True)
        try:
            response = requests.get(f"{API_URL}?date={date_str}&language=en")
            if response.status_code != 200: continue
                
            data = response.json()
            combinations = data.get('combinations', [])
            
            # Count unique matches used in combinations
            unique_ids = set()
            for combo in combinations:
                for m in combo.get('matches', []):
                    unique_ids.add(m.get('fixture_id'))
            
            count_for_day = len(unique_ids)
            total_filter_qualified += count_for_day
            total_combinations += len(combinations)
            
            # print(f"Qualified: {count_for_day}")

        except Exception as e:
            print(f"Error: {e}")

    # Estimates based on 12 leagues x 10 matches/week = 120 matches/weekend
    estimated_total_matches = WEEKS_BACK * 120 
    
    # ML Qualified is roughly 20-30% of total matches usually, but we can't see it without API logs.
    # We will report what we know.

    print("\n" + "="*50)
    print("FUNNEL ANALYSIS (Last 10 Weeks)")
    print("="*50)
    print(f"Days Checked: {len(dates)}")
    print(f"Estimated Total Matches (12 Leagues): ~{estimated_total_matches}")
    print(f"Matches Qualified by Filters (>1.68 Odds): {total_filter_qualified}")
    print(f"Total Combinations Generated: {total_combinations}")
    print("-" * 30)
    print(f"Filter Acceptance Rate: {total_filter_qualified / estimated_total_matches * 100:.2f}% (Approx)")
    print("="*50)

if __name__ == "__main__":
    run_funnel_analysis()
