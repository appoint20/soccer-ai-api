"""
Team Stats Service - Load real stats from Football API JSON files
"""
import json
from pathlib import Path
from typing import Dict, List, Optional


DATA_DIR = Path(__file__).parent.parent.parent / "data"


class TeamStatsService:
    """
    Load team statistics from pre-fetched Football API JSON files.
    
    Structure: data/team_stats/{folder_name}/2025/teams/{team_id}_stats.json
    """
    
    def __init__(self):
        self.leagues_config: List[Dict] = []
        self.team_stats_cache: Dict[str, Dict] = {}  # Cache: "league_id:team_id" -> stats
        self._load_leagues()
    
    def _load_leagues(self):
        """Load league configuration with folder mappings."""
        leagues_file = DATA_DIR / "leagues.json"
        if leagues_file.exists():
            with open(leagues_file, 'r') as f:
                self.leagues_config = json.load(f)
    
    def _get_folder_name(self, league_id: str) -> Optional[str]:
        """Get folder name for a league ID (e.g., E0 -> Premier_League)."""
        for league in self.leagues_config:
            if league.get("id") == league_id:
                return league.get("folder_name")
        return None
    
    def _load_team_files(self, folder_name: str) -> List[Dict]:
        """Load all team stats files for a league."""
        teams_dir = DATA_DIR / "team_stats" / folder_name / "2025" / "teams"
        
        if not teams_dir.exists():
            return []
        
        team_files = list(teams_dir.glob("*_stats.json"))
        teams = []
        
        for tf in team_files:
            try:
                with open(tf, 'r') as f:
                    stats = json.load(f)
                    teams.append(stats)
            except Exception:
                continue
        
        return teams
    
    def get_team_stats(self, team_name: str, league_id: str, before_date=None) -> Dict:
        """
        Get team statistics from Football API JSON files.
        
        Args:
            team_name: Team name to search for
            league_id: League ID (e.g., "E0" for Premier League)
            before_date: Ignored (stats are current season)
        
        Returns:
            Dict with team statistics
        """
        cache_key = f"{league_id}:{team_name}"
        
        # Check cache
        if cache_key in self.team_stats_cache:
            return self.team_stats_cache[cache_key]
        
        # Get folder name for league
        folder_name = self._get_folder_name(league_id)
        if not folder_name:
            return self._empty_stats()
        
        # Load all teams for this league
        all_teams = self._load_team_files(folder_name)
        
        # Find matching team (fuzzy match on name)
        for team_data in all_teams:
            team_info = team_data.get("team", {})
            if self._match_team_name(team_name, team_info.get("name", "")):
                stats = self._parse_api_stats(team_data)
                self.team_stats_cache[cache_key] = stats
                return stats
        
        return self._empty_stats()
    
    def _match_team_name(self, search: str, actual: str) -> bool:
        """Fuzzy match team names (handles variations like 'Man United' vs 'Manchester United')."""
        search_lower = search.lower().strip()
        actual_lower = actual.lower().strip()
        
        # Exact match
        if search_lower == actual_lower:
            return True
        
        # Contains match (Man United in Manchester United)
        if search_lower in actual_lower or actual_lower in search_lower:
            return True
        
        # Common abbreviations
        abbrevs = {
            "man united": "manchester united",
            "man city": "manchester city",
            "spurs": "tottenham",
            "wolves": "wolverhampton",
            "villa": "aston villa",
            "west ham": "west ham united",
            "newcastle": "newcastle united",
            "brighton": "brighton and hove albion",
            "nottm forest": "nottingham forest",
            "nott'm forest": "nottingham forest",
        }
        
        for abbrev, full in abbrevs.items():
            if (search_lower == abbrev and full in actual_lower) or \
               (abbrev in search_lower and full in actual_lower):
                return True
        
        return False
    
    def _parse_api_stats(self, data: Dict) -> Dict:
        """Parse Football API response into our stats format."""
        team = data.get("team", {})
        fixtures = data.get("fixtures", {})
        goals = data.get("goals", {})
        form_str = data.get("form", "")
        
        # Parse form string into list
        form = list(form_str[-5:]) if form_str else ["N", "N", "N", "N", "N"]
        
        played = fixtures.get("played", {}).get("total", 0)
        wins = fixtures.get("wins", {}).get("total", 0)
        draws = fixtures.get("draws", {}).get("total", 0)
        losses = fixtures.get("loses", {}).get("total", 0)
        
        goals_for = goals.get("for", {}).get("total", {}).get("total", 0)
        goals_against = goals.get("against", {}).get("total", {}).get("total", 0)
        
        avg_goals_for = float(goals.get("for", {}).get("average", {}).get("total", "0") or "0")
        avg_goals_against = float(goals.get("against", {}).get("average", {}).get("total", "0") or "0")
        
        clean_sheet = data.get("clean_sheet", {}).get("total", 0)
        
        return {
            "team_id": team.get("id"),
            "team_name": team.get("name"),
            "logo": team.get("logo"),
            "played": played,
            "wins": wins,
            "draws": draws,
            "losses": losses,
            "goals_for": goals_for,
            "goals_against": goals_against,
            "goal_difference": goals_for - goals_against,
            "form": form,
            "clean_sheets": clean_sheet,
            "avg_goals_scored": round(avg_goals_for, 2),
            "avg_goals_conceded": round(avg_goals_against, 2)
        }
    
    def _empty_stats(self) -> Dict:
        """Return empty stats for unknown teams."""
        return {
            "team_id": None,
            "team_name": None,
            "logo": None,
            "played": 0,
            "wins": 0,
            "draws": 0,
            "losses": 0,
            "goals_for": 0,
            "goals_against": 0,
            "goal_difference": 0,
            "form": ["N", "N", "N", "N", "N"],
            "clean_sheets": 0,
            "avg_goals_scored": 0,
            "avg_goals_conceded": 0
        }


# Singleton instance
_team_stats_service = None


def get_team_stats_service() -> TeamStatsService:
    """Get singleton instance of TeamStatsService."""
    global _team_stats_service
    if _team_stats_service is None:
        _team_stats_service = TeamStatsService()
    return _team_stats_service
