from typing import List, Optional, Protocol
from datetime import date, datetime
import csv
import glob
from src.domain.entities.match import Match
from src.domain.services.team_name_matcher import TeamNameMatcher
from src.utils.logger import get_logger

logger = get_logger("HistoricalMatchRepository")

class IHistoricalMatchRepository(Protocol):
    """Interface for accessing historical match data."""
    
    def get_all(self) -> List[Match]:
        """Get all historical matches."""
        ...
    
    def find_by_teams_and_date(
        self,
        home_team: str,
        away_team: str,
        match_date: date,
        date_tolerance_days: int = 1
    ) -> Optional[Match]:
        """Find a specific match with fuzzy date matching."""
        ...


class CSVHistoricalMatchRepository:
    """
    Repository that loads historical matches from CSV files.
    
    Responsibilities:
    - Load CSV files
    - Parse match data
    - Cache results
    - Provide query methods
    """
    
    def __init__(self, csv_pattern: str = "data/raw/historical/*.csv", team_matcher: Optional[TeamNameMatcher] = None):
        self._csv_pattern = csv_pattern
        self._cache: Optional[List[Match]] = None
        self._matcher = team_matcher or TeamNameMatcher()
    
    def get_all(self) -> List[Match]:
        """Get all historical matches (cached)."""
        if self._cache is None:
            self._cache = self._load_from_csv()
        return self._cache
    
    def find_by_teams_and_date(
        self,
        home_team: str,
        away_team: str,
        match_date: date,
        date_tolerance_days: int = 1
    ) -> Optional[Match]:
        """Find match with fuzzy matching."""
        all_matches = self.get_all()
        
        for match in all_matches:
            if self._matcher.matches(match.home_team, home_team) and \
               self._matcher.matches(match.away_team, away_team) and \
               self._is_date_within_tolerance(match.match_date, match_date, date_tolerance_days):
                return match
        
        return None
    
    def _load_from_csv(self) -> List[Match]:
        """Load all matches from CSV files."""
        matches = []
        
        for csv_path in glob.glob(self._csv_pattern):
            try:
                with open(csv_path, 'r', encoding='utf-8') as f:
                    reader = csv.DictReader(f)
                    for row in reader:
                        match = self._parse_row(row)
                        if match:
                            matches.append(match)
            except Exception as e:
                # Log but continue with other files
                logger.warning(f"Error loading {csv_path}: {e}")
        
        logger.info(f"Loaded {len(matches)} historical matches from CSV")
        return matches
    
    def _parse_row(self, row: dict) -> Optional[Match]:
        """Parse CSV row into Match entity."""
        try:
            date_str = row.get('Date', '')
            match_date = self._parse_date(date_str)
            if not match_date:
                return None

            return Match(
                home_team=row.get('HomeTeam', ''),
                away_team=row.get('AwayTeam', ''),
                match_date=match_date,
                league=row.get('Div', 'Unknown'), # Default if not present
                season="Unknown", # Not in typical euro data csv without parsing filename/date
                fthg=int(row.get('FTHG', 0) or 0),
                ftag=int(row.get('FTAG', 0) or 0),
                ftr=row.get('FTR', ''),
                # Parse additional fields if necessary, but critical ones are above
            )
        except (ValueError, KeyError) as e:
            # logger.warning(f"Invalid row data: {e}") # Optional: reduce log spam
            return None
    
    def _parse_date(self, date_str: str) -> Optional[date]:
        """Parse date string to date object."""
        try:
            # Usually DD/MM/YYYY or YYYY-MM-DD
            if '-' in date_str:
                return datetime.strptime(date_str, "%Y-%m-%d").date()
            elif '/' in date_str:
                # Handle varying formats if needed, but standard euro data is DD/MM/YY usually
                # Let's try standard formats
                try:
                    return datetime.strptime(date_str, "%d/%m/%Y").date()
                except ValueError:
                     return datetime.strptime(date_str, "%d/%m/%y").date()
            return None
        except ValueError:
            return None

    def _is_date_within_tolerance(
        self,
        match_date: date,
        target_date: date,
        tolerance_days: int
    ) -> bool:
        """Check if match date is within tolerance of target."""
        delta = abs((match_date - target_date).days)
        return delta <= tolerance_days
