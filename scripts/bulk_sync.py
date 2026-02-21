
import requests
import json
import time

BASE_URL = "http://localhost:5165/api/verify/sync/fixtures"

def sync_season(league_id, season):
    print(f"Syncing League {league_id}, Season {season}...")
    try:
        url = f"{BASE_URL}/{league_id}?season={season}"
        response = requests.post(url, timeout=300) # 5 min timeout
        if response.status_code == 200:
            data = response.json()
            created = data.get('created', 0)
            print(f"  -> Success: Created {created} fixtures.")
        else:
            print(f"  -> Failed: Status {response.status_code}, {response.text}")
    except Exception as e:
        print(f"  -> Error: {str(e)}")

# Define the sync plan
sync_plan = [
    # Ligue 1 (61) - Missing 23, 22, 21
    (61, 2023), (61, 2022), (61, 2021),
    
    # Ligue 2 (62) - Missing 23, 22, 21
    (62, 2023), (62, 2022), (62, 2021),
    
    # Serie A (135) - Missing 24, 22, 21 (23 exists)
    (135, 2024), (135, 2022), (135, 2021),
    
    # Serie B (136) - Missing 24, 22, 21
    (136, 2024), (136, 2023), (136, 2022), (136, 2021),
    
    # La Liga 2 (141) - Missing 24, 22, 21
    (141, 2024), (141, 2023), (141, 2022), (141, 2021),

    # League 104 (Requested by user)
    (104, 2025), (104, 2024), (104, 2023)
]

print(f"Starting batch sync for {len(sync_plan)} items...")

for league_id, season in sync_plan:
    sync_season(league_id, season)
    time.sleep(1) # Small delay between requests

print("Batch sync completed.")
