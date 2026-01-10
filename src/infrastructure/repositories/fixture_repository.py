from typing import List, Optional, Protocol
from pathlib import Path
import csv
from datetime import datetime, time

from src.domain.entities.match import Match
from src.domain.value_objects.analysis_date import AnalysisDate
from src.utils.logger import get_logger

logger = get_logger("FixtureRepository")

class IFixtureRepository(Protocol):
    """Interface for loading upcoming fixtures."""
    
    def get_fixtures_for_date(self, date: AnalysisDate) -> List[Match]:
        """Get fixtures for a specific date."""
        ...
    
    def get_available_dates(self) -> List[AnalysisDate]:
        """Get all dates with available fixtures."""
        ...


class CSVFixtureRepository:
    """
    Repository that loads fixtures from CSV files.
    """
    
    def __init__(self, fixtures_dir: str = "data/raw/upcoming/daily"):
        self._fixtures_dir = Path(fixtures_dir)
    
    def get_fixtures_for_date(self, date: AnalysisDate) -> List[Match]:
        """Get fixtures for a specific date."""
        filename = f"fixtures_{date.to_string()}.csv"
        filepath = self._fixtures_dir / filename
        
        if not filepath.exists():
            raise FileNotFoundError(f"Fixture file not found: {filepath}")
        
        return self._load_from_csv(filepath)
    
    def get_available_dates(self) -> List[AnalysisDate]:
        """Get all dates with available fixtures."""
        if not self._fixtures_dir.exists():
            return []
        
        dates = []
        for filepath in sorted(self._fixtures_dir.glob("fixtures_*.csv")):
            try:
                date_obj = AnalysisDate.from_filename(filepath.name)
                dates.append(date_obj)
            except ValueError:
                logger.warning(f"Invalid filename format: {filepath.name}")
        
        return dates
    
    def _load_from_csv(self, filepath: Path) -> List[Match]:
        """Load fixtures from CSV file."""
        fixtures = []
        
        with open(filepath, 'r', encoding='utf-8') as f:
            reader = csv.DictReader(f)
            for row in reader:
                try:
                    fixture = self._parse_row(row)
                    fixtures.append(fixture)
                except ValueError as e:
                    logger.warning(f"Invalid fixture data in {filepath}: {e}")
        
        return fixtures
    
    def _parse_row(self, row: dict) -> Match:
        """Parse CSV row into Match entity."""
        match_date = datetime.strptime(row['Date'], "%Y-%m-%d").date()
        
        match_time = None
        if row.get('Time'):
            try:
                match_time = datetime.strptime(row['Time'], "%H:%M").time()
            except ValueError:
                pass
        
        def parse_float(val):
            return float(val) if val and val.strip() else None

        return Match(
            home_team=row['HomeTeam'],
            away_team=row['AwayTeam'],
            match_date=match_date,
            match_time=match_time,
            league=row['Div'],
            season="2025", # Placeholder
            b365h=parse_float(row.get('B365H')),
            b365d=parse_float(row.get('B365D')),
            b365a=parse_float(row.get('B365A')),
            b365_over25=parse_float(row.get('B365>2.5')),
            b365_under25=parse_float(row.get('B365<2.5')),
        )
