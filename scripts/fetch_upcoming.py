
import requests
import json
from datetime import datetime

API_URL = "http://localhost:5165/api/combinations"
DATES = ["2026-02-06", "2026-02-07", "2026-02-08", "2026-02-09"]

def fetch_combinations():
    for date_str in DATES:
        print(f"\n{'='*70}")
        print(f" NEW COMBINATIONS FOR: {date_str}")
        print(f"{'='*70}")
        
        try:
            response = requests.get(f"{API_URL}?date={date_str}&language=en")
            if response.status_code != 200:
                print(f"Error {response.status_code}")
                continue
                
            data = response.json()
            combinations = data.get('combinations', [])
            
            if not combinations:
                print("No high-confidence combinations found for this date.")
                continue

            for i, combo in enumerate(combinations):
                name = combo.get('name', f'Combo #{i+1}')
                odds = combo.get('total_odds', 1.0)
                if odds > 50: odds = odds / 100.0
                
                print(f"\n{name:<25} | Total Odds: {odds:.2f}")
                
                for m in combo.get('matches', []):
                    home = m.get('home_team', 'Unknown')
                    away = m.get('away_team', 'Unknown')
                    market = m.get('market', '')
                    pred = m.get('prediction', '')
                    conf = m.get('confidence', 0) * 100
                    print(f"  - {home} vs {away} | {market}: {pred} ({conf:.1f}%)")

        except Exception as e:
            print(f"Error fetching for {date_str}: {e}")

if __name__ == "__main__":
    fetch_combinations()
