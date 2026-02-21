import urllib.request
import json
import ssl

# Configuration
API_URL = "http://localhost:5078/api/combinations"
DATE = "2026-02-08"

def fetch_today():
    print(f"\n{'='*60}")
    print(f"FETCHING FOR: {DATE}")
    print(f"{'='*60}")
    
    try:
        url = f"{API_URL}?date={DATE}&language=en"
        print(f"Requesting: {url}")
        
        # Create a context that ignores SSL verification (just in case, though it's http)
        ctx = ssl.create_default_context()
        ctx.check_hostname = False
        ctx.verify_mode = ssl.CERT_NONE
        
        with urllib.request.urlopen(url, context=ctx) as response:
            if response.status != 200:
                print(f"Error {response.status}")
                return
            
            data = json.loads(response.read().decode('utf-8'))
            combinations = data.get('combinations', [])
            
            if not combinations:
                print("No combinations found for this date.")
                return

            for i, combo in enumerate(combinations, 1):
                matches = combo.get('matches', [])
                if not matches:
                    continue
                
                print(f"\nCombo #{i}: {combo.get('name')}")
                print(f"Risk: {combo.get('risk_level')}")
                print(f"{'-'*40}")
                for m in matches:
                    home = m.get('home_team')
                    away = m.get('away_team')
                    market = m.get('market')
                    pred = m.get('prediction')
                    confidence = m.get('confidence', 0) * 100
                    odds = m.get('odds', 0)
                    
                    print(f"  ⚽ {home} vs {away}")
                    print(f"     Market: {market} | Prediction: {pred} | Confidence: {confidence:.1f}% | Odds: {odds:.2f}")

    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    fetch_today()
