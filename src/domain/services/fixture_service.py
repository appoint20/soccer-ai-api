from datetime import datetime, date
from typing import List, Dict, Any, Optional
from pathlib import Path

from src.utils.logger import get_logger
from src.data.loaders.csv_loader import CSVLoader

class FixtureService:
    """
    Service for loading and normalizing upcoming fixtures.
    Eliminates duplication of CSV loading logic across routers.
    """
    
    def __init__(self, fixtures_path: str = "data/raw/upcoming/fixtures_clean.csv"):
        self.logger = get_logger("FixtureService")
        self.fixtures_path = Path(fixtures_path)
        self.loader = CSVLoader()
        
    def load_upcoming_fixtures(self, target_date: Optional[str] = None) -> List[Dict[str, Any]]:
        """
        Load fixtures, normalize dates, and optionally filter by date.
        
        Args:
            target_date: Optional date string (YYYY-MM-DD) to filter by.
            
        Returns:
            List of normalized fixture dictionaries.
        """
        if not self.fixtures_path.exists():
            self.logger.warning(f"Fixtures file not found at {self.fixtures_path}")
            return []
            
        try:
            df = self.loader.load(self.fixtures_path)
            if df is None or df.empty:
                self.logger.warning("No fixtures loaded")
                return []
                
            fixtures = df.to_dict("records")
            normalized_fixtures = []
            
            for f in fixtures:
                # 1. Normalize Date to YYYY-MM-DD string
                # CSVLoader gives 'parsed_date' as date object, but we need consistent string for matching
                match_date = f.get("parsed_date") or f.get("date")
                
                if isinstance(match_date, (date, datetime)):
                    date_str = match_date.isoformat()[:10]
                else:
                    date_str = str(match_date)[:10]
                
                f["match_date"] = date_str
                
                # 2. Normalize Match ID for consistency
                # Format: Home-Away-Date (No Spaces)
                home = f.get("home_team", "").replace(" ", "")
                away = f.get("away_team", "").replace(" ", "")
                f["match_id"] = f"{home}-{away}-{date_str}"
                
                # Filter by date if requested
                if target_date and date_str != target_date:
                    continue
                    
                normalized_fixtures.append(f)
                
            self.logger.info(f"Loaded {len(normalized_fixtures)} fixtures (filter_date={target_date})")
            return normalized_fixtures
            
        except Exception as e:
            self.logger.error(f"Error loading fixtures: {e}")
            return []
