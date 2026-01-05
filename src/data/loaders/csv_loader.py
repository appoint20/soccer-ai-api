"""CSV loader for upcoming fixtures."""
from pathlib import Path
from typing import Optional, Union

import pandas as pd

from src.utils.logger import get_logger
from src.utils.helpers import parse_date, parse_time


# Required columns for upcoming fixtures
REQUIRED_COLUMNS = ["Date", "HomeTeam", "AwayTeam"]

# Team name normalization map (short name -> full name in historical data)
TEAM_NAME_MAP = {
    # England
    "Man City": "Manchester City",
    "Man United": "Manchester United",
    "Spurs": "Tottenham",
    "Wolves": "Wolverhampton",
    "West Ham": "West Ham United",
    "Sheffield Utd": "Sheffield United",
    "Sheffield Wed": "Sheffield Weds",
    "Sheff Utd": "Sheffield United",
    "Sheff Wed": "Sheffield Weds",
    "Sheff Wed": "Sheffield Weds",
    "Nott'm Forest": "Nott'm Forest",  # Keep as-is (matches historical)
    "West Brom": "West Bromwich",
    "QPR": "QPR",
    "Brighton": "Brighton",
    "Newcastle": "Newcastle",
    "Leicester": "Leicester",
    # Germany
    "Bayern": "Bayern Munich",
    "Dortmund": "Dortmund",
    "RB Leipzig": "RB Leipzig",
    "Leverkusen": "Leverkusen",
    "M'gladbach": "M'gladbach",
    # Spain
    "Ath Madrid": "Ath Madrid",
    "Ath Bilbao": "Ath Bilbao",
    "Real Madrid": "Real Madrid",
    # Italy
    "Inter": "Inter",
    "AC Milan": "Milan",
    "Juventus": "Juventus",
    # France
    "Paris SG": "Paris SG",
    "Lyon": "Lyon",
    "Marseille": "Marseille",
}


class CSVLoader:
    """
    Loads upcoming fixture data from CSV files.
    
    Handles reading CSV files containing future match fixtures
    with dates, times, and team information.
    """
    
    def __init__(self):
        """Initialize the CSV loader."""
        self.logger = get_logger("CSVLoader")
    
    def load(
        self,
        file_path: Union[str, Path],
    ) -> Optional[pd.DataFrame]:
        """
        Load upcoming fixtures from a CSV file.
        
        Expected columns: Date, Time, HomeTeam, AwayTeam, League
        
        Args:
            file_path: Path to the CSV file
            
        Returns:
            DataFrame with upcoming fixtures or None if failed
        """
        file_path = Path(file_path)
        
        if not file_path.exists():
            self.logger.error(f"File not found: {file_path}")
            return None
        
        try:
            # Load CSV with various encodings
            df = self._load_csv_with_fallback(file_path)
            
            if df is None or df.empty:
                self.logger.error(f"No data in file: {file_path}")
                return None
            
            self.logger.info(f"Loaded {len(df)} fixtures from {file_path.name}")
            
            # Validate required columns
            if not self._validate_columns(df, file_path):
                return None
            
            # Standardize column names
            df = self._standardize_columns(df)
            
            # Normalize team names (e.g., Man City -> Manchester City)
            df = self._normalize_team_names(df)
            
            # Parse dates and times
            df = self._parse_datetime(df)
            
            return df
            
        except Exception as e:
            self.logger.error(f"Failed to load {file_path}: {e}")
            return None
    
    def _load_csv_with_fallback(self, file_path: Path) -> Optional[pd.DataFrame]:
        """
        Try loading CSV with various encodings.
        
        Args:
            file_path: Path to CSV file
            
        Returns:
            DataFrame or None
        """
        encodings = ['utf-8', 'latin-1', 'cp1252']
        
        for encoding in encodings:
            try:
                df = pd.read_csv(file_path, encoding=encoding)
                return df
            except UnicodeDecodeError:
                continue
            except Exception as e:
                self.logger.error(f"CSV parse error with {encoding}: {e}")
                continue
        
        return None
    
    def _validate_columns(self, df: pd.DataFrame, file_path: Path) -> bool:
        """
        Validate required columns are present.
        
        Args:
            df: DataFrame to validate
            file_path: File path for error messages
            
        Returns:
            True if valid
        """
        # Check for required columns (case-insensitive)
        df_cols_lower = [c.lower() for c in df.columns]
        
        missing = []
        for col in REQUIRED_COLUMNS:
            if col.lower() not in df_cols_lower:
                missing.append(col)
        
        if missing:
            self.logger.error(
                f"Missing required columns in {file_path.name}: {missing}"
            )
            return False
        
        return True
    
    def _standardize_columns(self, df: pd.DataFrame) -> pd.DataFrame:
        """
        Standardize column names.
        
        Args:
            df: DataFrame with original columns
            
        Returns:
            DataFrame with standardized columns
        """
        # Create lowercase mapping
        rename_map = {}
        for col in df.columns:
            # Convert to snake_case
            new_name = col.strip().lower().replace(' ', '_')
            
            # Handle common variations
            if new_name in ('hometeam', 'home'):
                new_name = 'home_team'
            elif new_name in ('awayteam', 'away'):
                new_name = 'away_team'
            elif new_name in ('div', 'division'):
                new_name = 'league'
            
            rename_map[col] = new_name
        
        return df.rename(columns=rename_map)
    
    def _normalize_team_names(self, df: pd.DataFrame) -> pd.DataFrame:
        """
        Normalize team names to match historical data.
        
        Converts short names (e.g., 'Man City') to full names 
        used in historical data (e.g., 'Manchester City').
        
        Args:
            df: DataFrame with team columns
            
        Returns:
            DataFrame with normalized team names
        """
        for col in ['home_team', 'away_team']:
            if col in df.columns:
                df[col] = df[col].apply(
                    lambda x: TEAM_NAME_MAP.get(x, x) if pd.notna(x) else x
                )
        
        return df
    
    def _parse_datetime(self, df: pd.DataFrame) -> pd.DataFrame:
        """
        Parse date and time columns.
        
        Args:
            df: DataFrame with date/time strings
            
        Returns:
            DataFrame with parsed date/time
        """
        # Parse date
        if 'date' in df.columns:
            df['parsed_date'] = df['date'].apply(
                lambda x: parse_date(str(x)) if pd.notna(x) else None
            )
            
            # Log parsing issues
            null_dates = df['parsed_date'].isna().sum()
            if null_dates > 0:
                self.logger.warning(f"{null_dates} rows with unparseable dates")
        
        # Parse time
        if 'time' in df.columns:
            df['parsed_time'] = df['time'].apply(
                lambda x: parse_time(str(x)) if pd.notna(x) else None
            )
        
        return df


def load_upcoming_fixtures(
    file_path: Union[str, Path],
) -> Optional[pd.DataFrame]:
    """
    Load upcoming fixtures from a CSV file.
    
    Convenience function that creates a CSVLoader and loads data.
    
    Args:
        file_path: Path to the CSV file
        
    Returns:
        DataFrame with fixtures or None if failed
        
    Example:
        >>> df = load_upcoming_fixtures("data/raw/upcoming/fixtures.csv")
        >>> print(df[['date', 'home_team', 'away_team', 'league']])
    """
    loader = CSVLoader()
    return loader.load(file_path)
