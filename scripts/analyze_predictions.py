
import requests
import json
from collections import Counter

API_URL = "http://localhost:5165/api/fixtures/predictions"
DATES = ["2026-02-06", "2026-02-07", "2026-02-08", "2026-02-09"]
LEAGUES = [39, 40, 41, 42, 140, 141, 78, 79, 135, 136, 61, 62]

def analyze_distribution():
    all_over25 = []
    all_btts = []
    
    for date in DATES:
        for league in LEAGUES:
            try:
                url = f"{API_URL}?date={date}&leagueId={league}&language=en"
                resp = requests.get(url)
                if resp.status_code == 200:
                    data = resp.json()
                    predictions = data.get('predictions', [])
                    for p in predictions:
                        # Over 2.5
                        o2 = p.get('over25', {})
                        if o2:
                            pred_val = "Over 2.5" if o2.get('prediction') else "Under 2.5"
                            all_over25.append((pred_val, o2.get('confidence', 0)))
                            
                        # BTTS
                        bt = p.get('btts', {})
                        if bt:
                            pred_val = "BTTS Yes" if bt.get('prediction') else "BTTS No"
                            all_btts.append((pred_val, bt.get('confidence', 0)))
            except:
                pass

    print("\nOver 2.5 / Under 2.5 Distribution:")
    o_counts = Counter(m[0] for m in all_over25)
    for market, count in o_counts.items():
        avg_conf = sum(m[1] for m in all_over25 if m[0] == market) / count if count > 0 else 0
        print(f"  {market:<12}: {count:<3} matches | Avg Confidence: {avg_conf:.3f}")

    print("\nBTTS Yes / No Distribution:")
    b_counts = Counter(m[0] for m in all_btts)
    for market, count in b_counts.items():
        avg_conf = sum(m[1] for m in all_btts if m[0] == market) / count if count > 0 else 0
        print(f"  {market:<12}: {count:<3} matches | Avg Confidence: {avg_conf:.3f}")

if __name__ == "__main__":
    analyze_distribution()
