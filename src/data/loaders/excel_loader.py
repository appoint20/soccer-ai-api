"""Excel loader for historical match data."""
from pathlib import Path
from typing import Optional, Union

import pandas as pd

from src.utils.logger import get_logger
from src.utils.helpers import validate_league_code


# Column mapping from Excel to standardized names
COLUMN_MAPPING = {
    "Div": "league",
    "Date": "date",
    "Time": "time",
    "HomeTeam": "home_team",
    "AwayTeam": "away_team",
    "FTHG": "fthg",
    "FTAG": "ftag",
    "FTR": "ftr",
    "HTHG": "hthg",
    "HTAG": "htag",
    "HTR": "htr",
    "Referee": "referee",
    "HS": "hs",
    "AS": "as",
    "HST": "hst",
    "AST": "ast",
    "HF": "hf",
    "AF": "af",
    "HC": "hc",
    "AC": "ac",
    "HY": "hy",
    "AY": "ay",
    "HR": "hr",
    "AR": "ar",
    # 1X2 odds
    "B365H": "b365h",
    "B365D": "b365d",
    "B365A": "b365a",
    # Over/Under 2.5 goals odds
    "B365>2.5": "b365_over25",
    "B365<2.5": "b365_under25",
    "BbAv>2.5": "avg_over25",
    "BbAv<2.5": "avg_under25",
}

# Required columns for basic functionality
REQUIRED_COLUMNS = ["Date", "HomeTeam", "AwayTeam"]

# Supported league codes for filtering
SUPPORTED_LEAGUES = {
    "E0", "E1", "E2", "E3",  # England
    "D1",                     # Germany
    "F1", "F2",              # France
    "I1", "I2",              # Italy
    "SP1",                   # Spain
}


class ExcelLoader:
    """
    Loads historical match data from Excel files.
    
    Handles reading Excel/CSV files with football-data.co.uk format,
    standardizing column names, and validating data quality.
    """
    
    def __init__(self):
        """Initialize the Excel loader."""
        self.logger = get_logger("ExcelLoader")
    
    def load(
        self,
        file_path: Union[str, Path],
        league_code: Optional[str] = None,
        season: Optional[str] = None,
        filter_unsupported_leagues: bool = True,
    ) -> Optional[pd.DataFrame]:
        """
        Load historical data from an Excel or CSV file.
        
        Args:
            file_path: Path to the Excel/CSV file
            league_code: League code (optional, for validation)
            season: Season identifier (optional, added to data)
            filter_unsupported_leagues: If True, removes rows with unsupported league codes
            
        Returns:
            DataFrame with standardized columns or None if failed
        """
        file_path = Path(file_path)
        
        if not file_path.exists():
            self.logger.error(f"File not found: {file_path}")
            return None
        
        try:
            # Load based on file extension
            if file_path.suffix.lower() in ('.xlsx', '.xls'):
                df = pd.read_excel(file_path)
            elif file_path.suffix.lower() == '.csv':
                df = pd.read_csv(file_path, encoding='utf-8', on_bad_lines='skip')
            else:
                self.logger.error(f"Unsupported file format: {file_path.suffix}")
                return None
            
            self.logger.info(f"Loaded {len(df)} rows from {file_path.name}")
            
            # Validate required columns
            if not self._validate_columns(df, file_path):
                return None
            
            # Standardize column names
            df = self._standardize_columns(df)
            
            # Add league code if provided and not in data
            if league_code and 'league' not in df.columns:
                df['league'] = league_code
            elif league_code and 'league' in df.columns:
                # Validate league matches
                if not df['league'].iloc[0] == league_code:
                    self.logger.warning(
                        f"League code mismatch in {file_path.name}: "
                        f"expected {league_code}, got {df['league'].iloc[0]}"
                    )
            
            # Add season if provided
            if season:
                df['season'] = season
            
            # Validate league code if present
            if 'league' in df.columns and league_code:
                if not validate_league_code(league_code):
                    self.logger.warning(f"Unknown league code: {league_code}")
            
            # Filter unsupported leagues for faster processing
            if filter_unsupported_leagues and 'league' in df.columns:
                original_count = len(df)
                df = df[df['league'].isin(SUPPORTED_LEAGUES)]
                filtered_count = original_count - len(df)
                if filtered_count > 0:
                    self.logger.info(
                        f"Filtered {filtered_count} rows with unsupported leagues"
                    )
            
            return df
            
        except Exception as e:
            self.logger.error(f"Failed to load {file_path}: {e}")
            return None
    
    def _validate_columns(self, df: pd.DataFrame, file_path: Path) -> bool:
        """
        Validate that required columns are present.
        
        Args:
            df: DataFrame to validate
            file_path: File path for error messages
            
        Returns:
            True if valid, False otherwise
        """
        missing = []
        for col in REQUIRED_COLUMNS:
            if col not in df.columns:
                missing.append(col)
        
        if missing:
            self.logger.error(
                f"Missing required columns in {file_path.name}: {missing}"
            )
            return False
        
        return True
    
    def _standardize_columns(self, df: pd.DataFrame) -> pd.DataFrame:
        """
        Standardize column names to lowercase with underscores.
        
        Args:
            df: DataFrame with original column names
            
        Returns:
            DataFrame with standardized column names
        """
        # Create rename mapping for columns that exist
        rename_map = {}
        for original, standard in COLUMN_MAPPING.items():
            if original in df.columns:
                rename_map[original] = standard
        
        df = df.rename(columns=rename_map)
        
        return df
    
    def load_multiple(
        self,
        file_paths: list[Union[str, Path]],
        league_code: Optional[str] = None,
    ) -> Optional[pd.DataFrame]:
        """
        Load and combine data from multiple files.
        
        Args:
            file_paths: List of file paths to load
            league_code: League code for all files
            
        Returns:
            Combined DataFrame or None if all failed
        """
        all_dfs = []
        
        for path in file_paths:
            df = self.load(path, league_code)
            if df is not None:
                all_dfs.append(df)
        
        if not all_dfs:
            self.logger.error("No files loaded successfully")
            return None
        
        combined = pd.concat(all_dfs, ignore_index=True)
        self.logger.info(f"Combined {len(combined)} rows from {len(all_dfs)} files")
        
        return combined


def load_historical_data(
    file_path: Union[str, Path],
    league_code: Optional[str] = None,
    season: Optional[str] = None,
) -> Optional[pd.DataFrame]:
    """
    Load historical match data from an Excel file.
    
    Convenience function that creates an ExcelLoader and loads data.
    
    Args:
        file_path: Path to the Excel/CSV file
        league_code: League code (e.g., 'E0' for Premier League)
        season: Season identifier (e.g., '2024-25')
        
    Returns:
        DataFrame with standardized columns or None if failed
        
    Example:
        >>> df = load_historical_data("data/raw/historical/E0_2324.xlsx", "E0", "2023-24")
        >>> print(df.columns)
        Index(['league', 'date', 'time', 'home_team', 'away_team', ...])
    """
    loader = ExcelLoader()
    return loader.load(file_path, league_code, season)
