
import requests
import json
from datetime import datetime, timedelta

# Configuration
API_URL = "http://localhost:5165/api/combinations"
# Based on current time: 2026-02-06
DATES = ["2026-02-06", "2026-02-07", "2026-02-08", "2026-02-09"]

def fetch_upcoming():
    for date_str in DATES:
        print(f"\n{'='*60}")
        print(f"UPCOMING FOR: {date_str}")
        print(f"{'='*60}")
        
        try:
            response = requests.get(f"{API_URL}?date={date_str}&language=en")
            if response.status_code != 200:
                print(f"Error {response.status_code}")
                continue
                
            data = response.json()
            combinations = data.get('combinations', [])
            
            if not combinations:
                print("No combinations found for this date.")
                continue

            for i, combo in enumerate(combinations, 1):
                matches = combo.get('matches', [])
                if not matches:
                    continue
                
                print(f"\nCombo #{i}: {combo.get('name')}")
                print(f"{'-'*40}")
                for m in matches:
                    home = m.get('home_team')
                    away = m.get('away_team')
                    market = m.get('market')
                    pred = m.get('prediction')
                    confidence = m.get('confidence', 0) * 100
                    odds = m.get('odds', 0)
                    if odds > 50: odds /= 100.0
                    
                    print(f"  ⚽ {home} vs {away}")
                    print(f"     Market: {market} | Prediction: {pred} | Confidence: {confidence:.1f}% | Odds: {odds:.2f}")

        except Exception as e:
            print(f"Error: {e}")

if __name__ == "__main__":
    fetch_upcoming()
