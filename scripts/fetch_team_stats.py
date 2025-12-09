"""
Fetch Team Stats from Football API
Daily script to update team statistics for all leagues.

Usage:
    python scripts/fetch_team_stats.py --season 2025
"""
import os
import sys
import json
import time
import argparse
from pathlib import Path
from dotenv import load_dotenv

# Load environment variables
env_path = Path(__file__).parent.parent / ".env"
load_dotenv(env_path)

try:
    import requests
except ImportError:
    print("❌ requests library required. Run: pip install requests")
    sys.exit(1)


# Configuration from environment
API_KEY = os.getenv("FOOTBALL_API_KEY", "")
API_HOST = "api-football-v1.p.rapidapi.com"
BASE_URL = f"https://{API_HOST}/v3"

# Paths
PROJECT_ROOT = Path(__file__).parent.parent
DATA_DIR = PROJECT_ROOT / "data"
LEAGUES_FILE = DATA_DIR / "leagues.json"
TEAM_STATS_DIR = DATA_DIR / "team_stats"

# Rate limiting
DAILY_CALL_LIMIT = 95
CALL_DELAY_SECONDS = 1.1


class TeamStatsFetcher:
    """Fetches team statistics from Football API for all configured leagues."""
    
    def __init__(self):
        if not API_KEY:
            raise ValueError("FOOTBALL_API_KEY not set in environment. Add to .env file.")
        
        self.headers = {
            'x-rapidapi-key': API_KEY,
            'x-rapidapi-host': API_HOST
        }
        self.calls_made = 0
    
    def _make_request(self, endpoint: str, params: dict = None) -> dict | None:
        """Make rate-limited API request with error handling."""
        if self.calls_made >= DAILY_CALL_LIMIT:
            print(f"⚠️  Daily limit reached ({self.calls_made}). Stopping.")
            return None
        
        print(f"📡 API: {endpoint} {params or {}}")
        
        try:
            time.sleep(CALL_DELAY_SECONDS)
            response = requests.get(f"{BASE_URL}{endpoint}", headers=self.headers, params=params)
            self.calls_made += 1
            
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
    
    def load_leagues(self) -> list:
        """Load league configuration from leagues.json."""
        if not LEAGUES_FILE.exists():
            print(f"❌ Leagues file not found: {LEAGUES_FILE}")
            return []
        
        with open(LEAGUES_FILE, 'r') as f:
            return json.load(f)
    
    def get_teams_from_standings(self, standings_file: Path) -> list:
        """Extract team IDs from local standings file."""
        if not standings_file.exists():
            print(f"⚠️  Standings file not found: {standings_file}")
            return []
        
        try:
            with open(standings_file, 'r') as f:
                data = json.load(f)
            
            # Handle API response wrapper
            if isinstance(data, dict) and "response" in data:
                data = data["response"]
            
            if not data or not isinstance(data, list):
                return []
            
            league_node = data[0].get("league", {})
            standings_groups = league_node.get("standings", [])
            
            teams = []
            for group in standings_groups:
                for entry in group:
                    team = entry.get("team")
                    if team:
                        teams.append({'id': team['id'], 'name': team['name']})
            
            return teams
        except Exception as e:
            print(f"❌ Error parsing standings: {e}")
            return []
    
    def fetch_for_season(self, season: int):
        """Fetch team stats for all leagues for a given season."""
        leagues = self.load_leagues()
        
        if not leagues:
            print("❌ No leagues configured")
            return
        
        print(f"🎯 Fetching stats for Season {season}")
        print(f"   Leagues: {[l.get('name') for l in leagues]}")
        
        for league in leagues:
            api_id = league.get("api_id")
            folder_name = league.get("folder_name")
            league_name = league.get("name")
            
            if not api_id or not folder_name:
                print(f"⚠️  Skipping {league_name}: missing api_id or folder_name")
                continue
            
            # Setup directories
            league_dir = TEAM_STATS_DIR / folder_name
            standings_file = league_dir / f"{season}.json"
            teams_dir = league_dir / str(season) / "teams"
            teams_dir.mkdir(parents=True, exist_ok=True)
            
            print(f"\n🏆 {league_name} (API ID: {api_id})")
            
            # Get teams from standings
            teams = self.get_teams_from_standings(standings_file)
            if not teams:
                print(f"   ⚠️  No teams found. Fetch standings first.")
                continue
            
            print(f"   📋 {len(teams)} teams")
            
            # Fetch stats for each team
            for team in teams:
                team_id = team['id']
                team_name = team['name']
                stats_file = teams_dir / f"{team_id}_stats.json"
                
                # Skip if cached
                if stats_file.exists():
                    print(f"   ✓ {team_name}: cached")
                    continue
                
                # Fetch from API
                print(f"   ↓ {team_name}...")
                stats = self._make_request("/teams/statistics", {
                    "league": api_id,
                    "season": season,
                    "team": team_id
                })
                
                if stats:
                    with open(stats_file, 'w') as f:
                        json.dump(stats, f, indent=4)
                    print(f"     ✅ Saved")
                else:
                    print(f"     ❌ Failed")
        
        print(f"\n✅ Complete. API calls: {self.calls_made}")


def main():
    parser = argparse.ArgumentParser(description="Fetch team statistics from Football API")
    parser.add_argument("--season", type=int, required=True, help="Season year (e.g. 2025)")
    args = parser.parse_args()
    
    if not API_KEY:
        print("❌ FOOTBALL_API_KEY not set in .env file")
        print("   Add: FOOTBALL_API_KEY=your_key_here")
        sys.exit(1)
    
    fetcher = TeamStatsFetcher()
    fetcher.fetch_for_season(args.season)


if __name__ == "__main__":
    main()
