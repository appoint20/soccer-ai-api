"""
Match Repository Implementations.

These implement the repository interfaces defined in the application layer.
They handle data access concerns (file I/O, parsing, caching).
"""
from datetime import date, datetime, time
from pathlib import Path
from typing import List, Optional

import pandas as pd

from src.domain.entities.match import Match
from src.utils.logger import get_logger

logger = get_logger("MatchRepository")


# ============== League Name Mapping ==============

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

SUPPORTED_LEAGUES = list(LEAGUE_NAMES.keys())


# ============== Upcoming Match Repository ==============

class UpcomingMatchRepository:
    """
    Repository for upcoming matches (fixtures).
    
    Reads from fixtures_clean.csv and converts to Match entities.
    Implements IUpcomingMatchRepository protocol.
    """
    
    def __init__(self, fixtures_path: str = "data/raw/upcoming/fixtures_clean.csv"):
        self._fixtures_path = Path(fixtures_path)
    
    def get_by_date(self, target_date: Optional[str] = None) -> List[Match]:
        """Get upcoming matches, optionally filtered by date."""
        all_matches = self.get_all()
        
        if not target_date:
            return all_matches
        
        try:
            filter_date = datetime.strptime(target_date, "%Y-%m-%d").date()
            return [m for m in all_matches if m.match_date == filter_date]
        except ValueError:
            logger.warning(f"Invalid date format: {target_date}")
            return all_matches
    
    def get_all(self) -> List[Match]:
        """Load all upcoming matches from CSV."""
        if not self._fixtures_path.exists():
            logger.warning(f"Fixtures file not found: {self._fixtures_path}")
            return []
        
        try:
            df = pd.read_csv(self._fixtures_path)
            matches = []
            
            for _, row in df.iterrows():
                match = self._row_to_match(row)
                if match:
                    matches.append(match)
            
            logger.info(f"Loaded {len(matches)} upcoming matches")
            return matches
            
        except Exception as e:
            logger.error(f"Failed to load fixtures: {e}")
            return []
    
    def _row_to_match(self, row: pd.Series) -> Optional[Match]:
        """Convert CSV row to Match entity."""
        try:
            # Parse date
            date_val = row.get("Date") or row.get("date")
            if pd.isna(date_val):
                return None
            
            if isinstance(date_val, str):
                for fmt in ["%Y-%m-%d", "%d/%m/%Y", "%d/%m/%y"]:
                    try:
                        match_date = datetime.strptime(date_val, fmt).date()
                        break
                    except ValueError:
                        continue
                else:
                    return None
            else:
                match_date = pd.to_datetime(date_val).date()
            
            # Parse time
            time_val = row.get("Time") or row.get("time")
            match_time = None
            if time_val and not pd.isna(time_val):
                try:
                    parts = str(time_val).split(":")
                    match_time = time(int(parts[0]), int(parts[1]))
                except:
                    pass
            
            # Get league
            league = row.get("Div") or row.get("league") or row.get("League") or ""
            
            # Get teams
            home_team = row.get("HomeTeam") or row.get("home_team") or ""
            away_team = row.get("AwayTeam") or row.get("away_team") or ""
            
            if not home_team or not away_team:
                return None
            
            # Generate match ID
            match_id = f"{match_date.isoformat()}_{home_team}_vs_{away_team}_{league}"
            
            return Match(
                id=match_id,
                home_team=home_team,
                away_team=away_team,
                match_date=match_date,
                match_time=match_time,
                league=league,
                season="2025-26",
            )
            
        except Exception as e:
            logger.debug(f"Failed to parse row: {e}")
            return None


# ============== Historical Match Repository ==============

class HistoricalMatchRepository:
    """
    Repository for historical match data.
    
    Reads from JSON storage and converts to Match entities.
    Implements IHistoricalMatchRepository protocol.
    """
    
    def __init__(self, matches: Optional[List[dict]] = None):
        """
        Initialize with historical matches data.
        
        Args:
            matches: List of match dictionaries (loaded at startup)
        """
        self._matches_data = matches or []
        self._matches: Optional[List[Match]] = None
    
    def set_matches(self, matches: List[dict]) -> None:
        """Set the historical matches data."""
        self._matches_data = matches
        self._matches = None  # Reset cache
        logger.info(f"HistoricalMatchRepository: Loaded {len(matches)} matches")
    
    def get_all(self) -> List[Match]:
        """Get all historical matches as entities."""
        if self._matches is None:
            self._matches = self._convert_to_entities()
        return self._matches
    
    def get_by_team(self, team_name: str, last_n: int = 5) -> List[Match]:
        """Get recent matches for a team."""
        all_matches = self.get_all()
        team_lower = team_name.lower().strip()
        
        team_matches = [
            m for m in all_matches
            if m.home_team.lower().strip() == team_lower
            or m.away_team.lower().strip() == team_lower
        ]
        
        # Sort by date descending
        team_matches.sort(key=lambda m: m.match_date or date.min, reverse=True)
        
        return team_matches[:last_n]

    def find_by_teams_and_date(self, home_team: str, away_team: str, match_date: date) -> Optional[Match]:
        """Find a specific historical match."""
        all_matches = self.get_all()
        home_lower = home_team.lower().strip()
        away_lower = away_team.lower().strip()
        
        for match in all_matches:
            if (match.match_date == match_date and 
                match.home_team.lower() == home_lower and 
                match.away_team.lower() == away_lower):
                return match
        return None
    
    def get_h2h(self, team_a: str, team_b: str, last_n: int = 5) -> List[Match]:
        """Get head-to-head matches between two teams."""
        all_matches = self.get_all()
        a_lower = team_a.lower().strip()
        b_lower = team_b.lower().strip()
        
        h2h_matches = [
            m for m in all_matches
            if {m.home_team.lower().strip(), m.away_team.lower().strip()} == {a_lower, b_lower}
        ]
        
        # Sort by date descending
        h2h_matches.sort(key=lambda m: m.match_date or date.min, reverse=True)
        
        return h2h_matches[:last_n]
    
    def _convert_to_entities(self) -> List[Match]:
        """Convert raw dict data to Match entities."""
        matches = []
        
        for data in self._matches_data:
            match = self._dict_to_match(data)
            if match:
                matches.append(match)
        
        return matches
    
    def _dict_to_match(self, data: dict) -> Optional[Match]:
        """Convert dictionary to Match entity."""
        try:
            # Parse date
            date_val = data.get("match_date") or data.get("date") or data.get("Date")
            if not date_val:
                return None
            
            if isinstance(date_val, str):
                for fmt in ["%Y-%m-%d", "%d/%m/%Y", "%d/%m/%y"]:
                    try:
                        match_date = datetime.strptime(date_val[:10], fmt).date()
                        break
                    except ValueError:
                        continue
                else:
                    return None
            elif isinstance(date_val, date):
                match_date = date_val
            else:
                return None
            
            # Get teams - handle both key formats
            home_team = data.get("home_team") or data.get("HomeTeam") or ""
            away_team = data.get("away_team") or data.get("AwayTeam") or ""
            
            if not home_team or not away_team:
                return None
            
            # Get goals - handle both key formats
            fthg = data.get("fthg") or data.get("FTHG")
            ftag = data.get("ftag") or data.get("FTAG")
            
            if fthg is not None:
                fthg = int(fthg)
            if ftag is not None:
                ftag = int(ftag)
            
            # Get result
            ftr = data.get("ftr") or data.get("FTR")
            
            # Get league
            league = data.get("league") or data.get("Div") or ""
            
            # Get ID
            match_id = data.get("id") or data.get("match_id") or ""
            if not match_id:
                match_id = f"{match_date}_{home_team}_vs_{away_team}_{league}"
            
            return Match(
                id=match_id,
                home_team=home_team,
                away_team=away_team,
                match_date=match_date,
                league=league,
                season=data.get("season", ""),
                fthg=fthg,
                ftag=ftag,
                ftr=ftr,
            )
            
        except Exception as e:
            logger.debug(f"Failed to convert match: {e}")
            return None
