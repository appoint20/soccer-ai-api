
import requests
import time

BASE_URL = "http://localhost:5165/api/verify/sync/fixtures"

# Supported Leagues
LEAGUES = [
    39,  # Premier League
    40,  # Championship
    41,  # League One
    42,  # League Two
    140, # La Liga
    141, # La Liga 2
    78,  # Bundesliga
    79,  # 2. Bundesliga
    135, # Serie A
    136, # Serie B
    61,  # Ligue 1
    62   # Ligue 2
]

SEASON = 2025

def sync_season(league_id, season):
    print(f"Syncing League {league_id}, Season {season}...")
    try:
        url = f"{BASE_URL}/{league_id}?season={season}"
        response = requests.post(url, timeout=300)
        if response.status_code == 200:
            data = response.json()
            created = data.get('created', 0)
            updated = data.get('updated', 0)
            print(f"  -> Success: Created {created}, Updated {updated} fixtures.")
        else:
            print(f"  -> Failed: Status {response.status_code}, {response.text}")
    except Exception as e:
        print(f"  -> Error: {str(e)}")

print(f"Starting update for missing odds (Season {SEASON})...")

for league_id in LEAGUES:
    sync_season(league_id, SEASON)
    time.sleep(1)

print("Update completed.")
