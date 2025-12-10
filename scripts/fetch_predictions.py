"""
Fetch Predictions from Football API
Retrieves match predictions and odds from API-Football for upcoming fixtures.

Usage:
    python scripts/fetch_predictions.py --league 39 --date 2025-12-10
"""
import os
import sys
import json
import time
import argparse
from pathlib import Path
from datetime import datetime, timedelta
from dotenv import load_dotenv

# Load environment variables
env_path = Path(__file__).parent.parent / ".env"
load_dotenv(env_path)

try:
    import requests
except ImportError:
    print("❌ requests library required. Run: pip install requests")
    sys.exit(1)


# Configuration
API_KEY = os.getenv("FOOTBALL_API_KEY", "")
API_HOST = "api-football-v1.p.rapidapi.com"
BASE_URL = f"https://{API_HOST}/v3"

# Paths
PROJECT_ROOT = Path(__file__).parent.parent
DATA_DIR = PROJECT_ROOT / "data"
PREDICTIONS_DIR = DATA_DIR / "predictions"

# Rate limiting
CALL_DELAY_SECONDS = 1.5
DAILY_CALL_LIMIT = 90

# League ID to folder mapping
LEAGUE_FOLDERS = {
    39: 'Premier_League',
    40: 'Championship',
    41: 'League_One',
    42: 'League_Two',
    78: 'Bundesliga',
    79: '2_Bundesliga',
    135: 'Serie_A',
    136: 'Serie_B',
    61: 'Ligue_1',
    62: 'Ligue_2',
    140: 'La_Liga'
}


class PredictionsFetcher:
    """Fetches predictions and odds from Football API."""
    
    def __init__(self):
        if not API_KEY:
            raise ValueError("FOOTBALL_API_KEY not set in environment")
        
        self.headers = {
            'x-rapidapi-key': API_KEY,
            'x-rapidapi-host': API_HOST
        }
        self.calls_made = 0
        self.calls_since_pause = 0
    
    def _make_request(self, endpoint: str, params: dict = None) -> dict | None:
        """Make rate-limited API request."""
        if self.calls_made >= DAILY_CALL_LIMIT:
            print(f"⚠️ Daily limit reached ({self.calls_made}). Stopping.")
            return None
        
        # Wait 1 minute after every 20 calls
        if self.calls_since_pause >= 20:
            print(f"⏸️ Pause for rate limiting... (waiting 60 seconds after 20 calls)")
            time.sleep(60)
            self.calls_since_pause = 0
        
        print(f"📡 API: {endpoint} {params or {}}")
        
        try:
            time.sleep(CALL_DELAY_SECONDS)
            response = requests.get(f"{BASE_URL}{endpoint}", headers=self.headers, params=params)
            self.calls_made += 1
            self.calls_since_pause += 1
            
            if response.status_code in [401, 403]:
                print(f"❌ Unauthorized ({response.status_code}). Check API key.")
                return None
            
            response.raise_for_status()
            data = response.json()
            
            if data.get("errors"):
                print(f"❌ API Error: {data['errors']}")
                return None
            
            return data.get('response')
        except Exception as e:
            print(f"❌ Request failed: {e}")
            return None
    
    def fetch_fixtures(self, league_id: int, date: str) -> list:
        """Fetch fixtures for a league on a specific date."""
        params = {
            "league": league_id,
            "date": date,
            "season": 2025
        }
        return self._make_request("/fixtures", params) or []
    
    def fetch_predictions(self, fixture_id: int) -> dict | None:
        """Fetch predictions for a specific fixture."""
        params = {"fixture": fixture_id}
        result = self._make_request("/predictions", params)
        return result[0] if result else None
    
    def fetch_odds(self, fixture_id: int) -> dict | None:
        """Fetch betting odds for a fixture."""
        params = {"fixture": fixture_id}
        result = self._make_request("/odds", params)
        return result[0] if result else None
    
    def fetch_for_date(self, league_id: int, date: str):
        """Fetch all predictions for a league on a date."""
        folder_name = LEAGUE_FOLDERS.get(league_id, f"league_{league_id}")
        
        # Create output directory
        output_dir = PREDICTIONS_DIR / folder_name
        output_dir.mkdir(parents=True, exist_ok=True)
        
        print(f"\n🏆 Fetching predictions for {folder_name} on {date}")
        
        # Get fixtures
        fixtures = self.fetch_fixtures(league_id, date)
        if not fixtures:
            print(f"   ⚠️ No fixtures found")
            return
        
        print(f"   📋 Found {len(fixtures)} fixtures")
        
        # Fetch predictions for each fixture
        for fixture in fixtures:
            fixture_id = fixture.get('fixture', {}).get('id')
            home_team = fixture.get('teams', {}).get('home', {}).get('name', 'Unknown')
            away_team = fixture.get('teams', {}).get('away', {}).get('name', 'Unknown')
            
            output_file = output_dir / f"{date}_{fixture_id}.json"
            
            # Skip if cached
            if output_file.exists():
                print(f"   ✓ {home_team} vs {away_team}: cached")
                continue
            
            print(f"   ↓ {home_team} vs {away_team}...")
            
            # Fetch prediction
            prediction = self.fetch_predictions(fixture_id)
            
            if prediction:
                # Combine fixture info with prediction
                result = {
                    "fixture_id": fixture_id,
                    "date": date,
                    "home_team": home_team,
                    "away_team": away_team,
                    "league": folder_name,
                    "api_prediction": {
                        "winner": prediction.get('predictions', {}).get('winner', {}),
                        "win_or_draw": prediction.get('predictions', {}).get('win_or_draw'),
                        "under_over": prediction.get('predictions', {}).get('under_over'),
                        "goals": prediction.get('predictions', {}).get('goals', {}),
                        "advice": prediction.get('predictions', {}).get('advice'),
                        "percent": prediction.get('predictions', {}).get('percent', {})
                    },
                    "teams_comparison": prediction.get('comparison', {}),
                    "h2h": prediction.get('h2h', [])[:5],  # Last 5 H2H
                    "fetched_at": datetime.now().isoformat()
                }
                
                with open(output_file, 'w') as f:
                    json.dump(result, f, indent=2)
                print(f"     ✅ Saved")
            else:
                print(f"     ❌ No prediction available")
        
        print(f"\n✅ Complete. API calls: {self.calls_made}")


def main():
    parser = argparse.ArgumentParser(description="Fetch predictions from Football API")
    parser.add_argument("--league", type=int, help="League ID (e.g., 39 for Premier League)")
    parser.add_argument("--date", type=str, help="Date (YYYY-MM-DD). Default: today")
    parser.add_argument("--all", action="store_true", help="Fetch for all configured leagues")
    args = parser.parse_args()
    
    if not API_KEY:
        print("❌ FOOTBALL_API_KEY not set in .env file")
        print("   Add: FOOTBALL_API_KEY=your_key_here")
        sys.exit(1)
    
    # Default to today
    target_date = args.date or datetime.now().strftime("%Y-%m-%d")
    
    fetcher = PredictionsFetcher()
    
    if args.all:
        # Fetch for all leagues
        for league_id in LEAGUE_FOLDERS.keys():
            fetcher.fetch_for_date(league_id, target_date)
    elif args.league:
        fetcher.fetch_for_date(args.league, target_date)
    else:
        print("Usage:")
        print("  python fetch_predictions.py --league 39 --date 2025-12-10")
        print("  python fetch_predictions.py --all --date 2025-12-10")
        print("\nLeague IDs:")
        for lid, name in LEAGUE_FOLDERS.items():
            print(f"  {lid}: {name}")


if __name__ == "__main__":
    main()
