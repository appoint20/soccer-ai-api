"""
Excel to CSV Sanitization Service.

Converts Excel files to clean CSV with only:
- Supported leagues (E0, E1, E2, E3, D1, F1, F2, I1, I2, SP1)
- Used columns only (removing unused betting odds columns)
"""
from pathlib import Path
from typing import Optional, Union, List
import pandas as pd

from src.utils.logger import get_logger


# Supported league codes
SUPPORTED_LEAGUES = {
    "E0", "E1", "E2", "E3",  # England: Premier League, Championship, League One, League Two
    "D1",                     # Germany: Bundesliga
    "F1", "F2",              # France: Ligue 1, Ligue 2
    "I1", "I2",              # Italy: Serie A, Serie B
    "SP1",                   # Spain: La Liga
}

# Only columns that are actually used in the codebase
USED_COLUMNS = [
    # Core identifiers
    "Div",        # League code
    "Date",       # Match date
    "Time",       # Kickoff time
    "HomeTeam",   # Home team name
    "AwayTeam",   # Away team name
    
    # Score data (for historical matches)
    "FTHG",       # Full-time home goals
    "FTAG",       # Full-time away goals
    "HTHG",       # Half-time home goals
    "HTAG",       # Half-time away goals
    "FTR",        # Full-time result (H/D/A)
    "HTR",        # Half-time result
    
    # Bet365 odds (only bookmaker we use)
    "B365H",      # Home win odds
    "B365D",      # Draw odds
    "B365A",      # Away win odds
    "B365>2.5",   # Over 2.5 goals odds
    "B365<2.5",   # Under 2.5 goals odds
]

# Column name variations (for case-insensitive matching)
COLUMN_VARIATIONS = {
    "div": "Div",
    "date": "Date",
    "time": "Time",
    "hometeam": "HomeTeam",
    "awayteam": "AwayTeam",
    "fthg": "FTHG",
    "ftag": "FTAG",
    "hthg": "HTHG",
    "htag": "HTAG",
    "ftr": "FTR",
    "htr": "HTR",
    "b365h": "B365H",
    "b365d": "B365D",
    "b365a": "B365A",
}


class ExcelSanitizer:
    """
    Sanitizes Excel/CSV files by:
    1. Keeping only supported leagues
    2. Keeping only used columns
    3. Removing empty/invalid rows
    """
    
    def __init__(self):
        self.logger = get_logger("ExcelSanitizer")
    
    def sanitize(
        self,
        input_path: Union[str, Path],
        output_path: Optional[Union[str, Path]] = None,
        keep_all_leagues: bool = False,
    ) -> Optional[pd.DataFrame]:
        """
        Sanitize an Excel or CSV file.
        
        Args:
            input_path: Path to input Excel/CSV file
            output_path: Path for output CSV file (optional, auto-generated if not provided)
            keep_all_leagues: If True, don't filter leagues
            
        Returns:
            Sanitized DataFrame or None if failed
        """
        input_path = Path(input_path)
        
        if not input_path.exists():
            self.logger.error(f"File not found: {input_path}")
            return None
        
        try:
            # Load data
            df = self._load_file(input_path)
            if df is None or df.empty:
                self.logger.error(f"No data in file: {input_path}")
                return None
            
            original_rows = len(df)
            original_cols = len(df.columns)
            
            # Normalize column names
            df = self._normalize_columns(df)
            
            # Normalize Date format (CRITICAL FIX)
            df = self._normalize_dates(df)
            
            # Enforce numeric types for goals/odds (CRITICAL FIX)
            df = self._normalize_numeric(df)
            
            # Filter to supported leagues
            if not keep_all_leagues and "Div" in df.columns:
                df = self._filter_leagues(df)
            
            # Keep only used columns
            df = self._filter_columns(df)
            
            # Remove rows with missing essential data
            df = self._clean_rows(df)
            
            # Log summary
            self.logger.info(
                f"Sanitized: {original_rows} → {len(df)} rows, "
                f"{original_cols} → {len(df.columns)} columns"
            )
            
            # Save if output path provided
            if output_path:
                output_path = Path(output_path)
                output_path.parent.mkdir(parents=True, exist_ok=True)
                df.to_csv(output_path, index=False)
                self.logger.info(f"Saved to: {output_path}")
            
            return df
            
        except Exception as e:
            self.logger.error(f"Failed to sanitize {input_path}: {e}")
            return None
    
    def _load_file(self, file_path: Path) -> Optional[pd.DataFrame]:
        """Load Excel or CSV file, combining all sheets for Excel."""
        extension = file_path.suffix.lower()
        
        if extension in ('.xlsx', '.xls'):
            # Load all sheets and combine
            excel_file = pd.ExcelFile(file_path)
            all_dfs = []
            
            for sheet_name in excel_file.sheet_names:
                try:
                    sheet_df = pd.read_excel(excel_file, sheet_name=sheet_name)
                    # Add sheet name as Div if not present
                    if "Div" not in sheet_df.columns:
                        sheet_df["Div"] = sheet_name
                    all_dfs.append(sheet_df)
                except Exception as e:
                    self.logger.warning(f"Failed to load sheet {sheet_name}: {e}")
            
            if not all_dfs:
                return None
            
            return pd.concat(all_dfs, ignore_index=True)
        
        elif extension == '.csv':
            return pd.read_csv(file_path, encoding='utf-8', on_bad_lines='skip')
        
        else:
            self.logger.error(f"Unsupported format: {extension}")
            return None
    
    def _normalize_columns(self, df: pd.DataFrame) -> pd.DataFrame:
        """Normalize column names to standard format."""
        rename_map = {}
        
        for col in df.columns:
            col_lower = col.lower().strip()
            
            # Check for variations
            if col_lower in COLUMN_VARIATIONS:
                rename_map[col] = COLUMN_VARIATIONS[col_lower]
        
        return df.rename(columns=rename_map)
    
    def _filter_leagues(self, df: pd.DataFrame) -> pd.DataFrame:
        """Keep only rows from supported leagues."""
        if "Div" not in df.columns:
            return df
        
        before = len(df)
        df = df[df["Div"].isin(SUPPORTED_LEAGUES)]
        after = len(df)
        
        if before - after > 0:
            self.logger.info(f"Removed {before - after} rows from unsupported leagues")
        
        return df
    
    def _filter_columns(self, df: pd.DataFrame) -> pd.DataFrame:
        """Keep only used columns."""
        # Find columns that exist in both USED_COLUMNS and df
        existing = [col for col in USED_COLUMNS if col in df.columns]
        
        removed = set(df.columns) - set(existing)
        if removed:
            self.logger.info(f"Removed {len(removed)} unused columns: {list(removed)[:5]}...")
        
        return df[existing]
    
    def _clean_rows(self, df: pd.DataFrame) -> pd.DataFrame:
        """Remove rows with missing essential data."""
        essential = ["Date", "HomeTeam", "AwayTeam"]
        
        before = len(df)
        
        for col in essential:
            if col in df.columns:
                df = df[df[col].notna()]
                df = df[df[col].astype(str).str.strip() != ""]
        
        after = len(df)
        
        if before - after > 0:
            self.logger.info(f"Removed {before - after} rows with missing essential data")
        
        return df

    def _normalize_dates(self, df: pd.DataFrame) -> pd.DataFrame:
        """
        Normalize Date column to datetime objects.
        Standardizes format across different seasons/files.
        """
        if "Date" in df.columns:
            # Coerce errors to NaT, assume day-first (European standard)
            df["Date"] = pd.to_datetime(
                df["Date"],
                errors="coerce",
                dayfirst=True
            )
            # Remove rows where date parsing failed
            df = df[df["Date"].notna()]
        return df

    def _normalize_numeric(self, df: pd.DataFrame) -> pd.DataFrame:
        """
        Enforce numeric types for goals and odds columns.
        """
        numeric_columns = [
            "FTHG", "FTAG", "HTHG", "HTAG",
            "B365H", "B365D", "B365A",
            "B365>2.5", "B365<2.5",
        ]
        
        for col in numeric_columns:
            if col in df.columns:
                df[col] = pd.to_numeric(df[col], errors="coerce")
                
        return df
    
    def get_summary(self, df: pd.DataFrame) -> dict:
        """Get summary of the DataFrame."""
        summary = {
            "total_rows": len(df),
            "columns": list(df.columns),
            "leagues": {},
            "date_range": {},
        }
        
        if "Div" in df.columns:
            summary["leagues"] = df["Div"].value_counts().to_dict()
        
        if "Date" in df.columns:
            summary["date_range"] = {
                "min": str(df["Date"].min()),
                "max": str(df["Date"].max()),
            }
        
        return summary


def sanitize_excel_to_csv(
    input_path: Union[str, Path],
    output_path: Optional[Union[str, Path]] = None,
) -> Optional[pd.DataFrame]:
    """
    Convenience function to sanitize Excel to CSV.
    
    Args:
        input_path: Path to input Excel/CSV file
        output_path: Path for output CSV (auto-generated if not provided)
        
    Returns:
        Sanitized DataFrame
        
    Example:
        >>> df = sanitize_excel_to_csv("data/raw/upcoming/Latest_Results.xlsx")
        >>> df = sanitize_excel_to_csv("input.xlsx", "output_clean.csv")
    """
    sanitizer = ExcelSanitizer()
    return sanitizer.sanitize(input_path, output_path)
