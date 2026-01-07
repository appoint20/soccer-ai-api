"""
Upcoming Match Service.

Loads and filters upcoming matches from CSV with league filtering
and fail-fast logging for excluded matches.
"""
from datetime import date, datetime
from typing import List, Dict, Any, Optional, Set
from pathlib import Path

from src.utils.logger import get_logger
from src.data.loaders.csv_loader import CSVLoader


# Supported leagues for analysis
SUPPORTED_LEAGUES = ["E0", "E1", "E2", "E3", "D1", "SP1", "I1", "I2", "F1", "F2"]

# League code to full name mapping
LEAGUE_NAMES = {
    "E0": "Premier League",
    "E1": "Championship",
    "E2": "League One",
    "E3": "League Two",
    "D1": "Bundesliga",
    "SP1": "La Liga",
    "I1": "Serie A",
    "I2": "Serie B",
    "F1": "Ligue 1",
    "F2": "Ligue 2",
}


class UpcomingMatchService:
    """
    Service for loading upcoming matches with league filtering.
    
    Features:
    - League code filtering (E0, E1, E2, E3, D1, SP1, I1, I2, F1, F2)
    - Fail-fast logging for excluded matches
    - Date normalization
    - Match ID generation
    """
    
    def __init__(
        self,
        fixtures_path: str = "data/raw/upcoming/fixtures_clean.csv",
        supported_leagues: Optional[List[str]] = None,
    ):
        """
        Initialize service.
        
        Args:
            fixtures_path: Path to fixtures CSV file
            supported_leagues: Override default supported leagues
        """
        self.logger = get_logger("UpcomingMatchService")
        self.fixtures_path = Path(fixtures_path)
        self.loader = CSVLoader()
        self.supported_leagues = set(supported_leagues or SUPPORTED_LEAGUES)
    
    def get_upcoming_matches(
        self,
        target_date: Optional[str] = None,
    ) -> List[Dict[str, Any]]:
        """
        Load and filter upcoming matches.
        
        Args:
            target_date: Optional date filter (YYYY-MM-DD)
            
        Returns:
            List of normalized match dictionaries
        """
        all_matches = self._load_raw_matches()
        
        if not all_matches:
            return []
        
        # Filter by supported leagues with logging
        filtered_matches, excluded = self._filter_by_leagues(all_matches)
        
        if excluded:
            excluded_leagues = set(m.get("league", m.get("Div", "?")) for m in excluded)
            self.logger.info(
                f"Excluded {len(excluded)} matches due to unsupported leagues: {excluded_leagues}"
            )
        
        # Filter by date if requested
        if target_date:
            filtered_matches = [
                m for m in filtered_matches
                if m.get("match_date") == target_date
            ]
            self.logger.info(f"Filtered to {len(filtered_matches)} matches for date {target_date}")
        
        return filtered_matches
    
    def _load_raw_matches(self) -> List[Dict[str, Any]]:
        """Load raw matches from CSV."""
        if not self.fixtures_path.exists():
            self.logger.warning(f"Fixtures file not found: {self.fixtures_path}")
            return []
        
        try:
            df = self.loader.load(self.fixtures_path)
            if df is None or df.empty:
                self.logger.warning("No fixtures loaded from CSV")
                return []
            
            matches = df.to_dict("records")
            
            # Normalize each match
            normalized = []
            for m in matches:
                normalized.append(self._normalize_match(m))
            
            self.logger.info(f"Loaded {len(normalized)} raw matches from CSV")
            return normalized
            
        except Exception as e:
            self.logger.error(f"Error loading fixtures: {e}")
            return []
    
    def _normalize_match(self, match: Dict[str, Any]) -> Dict[str, Any]:
        """Normalize match data for consistent access."""
        # Normalize date
        match_date = match.get("parsed_date") or match.get("date") or match.get("Date")
        
        if isinstance(match_date, (date, datetime)):
            date_str = match_date.strftime("%Y-%m-%d") if isinstance(match_date, datetime) else match_date.isoformat()
        else:
            date_str = str(match_date)[:10] if match_date else None
        
        # Normalize teams
        home_team = match.get("home_team") or match.get("HomeTeam") or ""
        away_team = match.get("away_team") or match.get("AwayTeam") or ""
        
        # Normalize league
        league_code = match.get("league") or match.get("Div") or ""
        league_name = LEAGUE_NAMES.get(league_code, league_code)
        
        # Generate match ID
        home_clean = home_team.replace(" ", "")
        away_clean = away_team.replace(" ", "")
        match_id = f"{home_clean}-{away_clean}-{date_str}"
        
        # Time
        time_val = match.get("time") or match.get("Time") or ""
        
        return {
            **match,
            "match_id": match_id,
            "match_date": date_str,
            "home_team": home_team,
            "away_team": away_team,
            "league_code": league_code,
            "league": league_name,
            "time": str(time_val) if time_val else None,
        }
    
    def _filter_by_leagues(
        self,
        matches: List[Dict[str, Any]],
    ) -> tuple:
        """
        Filter matches by supported leagues.
        
        Returns:
            (included_matches, excluded_matches)
        """
        included = []
        excluded = []
        
        for match in matches:
            league_code = match.get("league_code", "")
            
            if league_code in self.supported_leagues:
                included.append(match)
            else:
                excluded.append(match)
        
        return included, excluded
    
    def get_league_name(self, league_code: str) -> str:
        """Get full league name from code."""
        return LEAGUE_NAMES.get(league_code, league_code)
