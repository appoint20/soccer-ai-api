"""Data processor for cleaning and converting match data."""
from datetime import date, time
from typing import Optional
import uuid

import pandas as pd

from src.domain.entities import Match
from src.utils.logger import get_logger
from src.utils.helpers import (
    standardize_team_name,
    calculate_season,
    parse_date,
    parse_time,
    safe_int,
    safe_float,
)


class DataProcessor:
    """
    Processes raw match data into clean, standardized format.
    
    Handles data cleaning, validation, feature derivation,
    and conversion to Match entities.
    """
    
    def __init__(self):
        """Initialize the data processor."""
        self.logger = get_logger("DataProcessor")
        self._team_name_cache: dict[str, str] = {}
        self._warnings: list[str] = []
    
    def process_historical_data(self, df: pd.DataFrame) -> pd.DataFrame:
        """
        Clean and standardize historical match data.
        
        Operations:
        - Drop rows with missing essential data
        - Standardize team names
        - Parse dates and times
        - Add derived features (total_goals, etc.)
        - Validate data quality
        
        Args:
            df: Raw DataFrame from Excel loader
            
        Returns:
            Cleaned DataFrame
        """
        if df is None or df.empty:
            self.logger.warning("Empty DataFrame provided")
            return pd.DataFrame()
        
        self.logger.info(f"Processing {len(df)} matches")
        original_count = len(df)
        
        # Make a copy to avoid modifying original
        df = df.copy()
        
        # Step 1: Drop rows with missing essential fields
        df = self._drop_invalid_rows(df)
        
        # Step 2: Parse dates
        df = self._parse_dates(df)
        
        # Step 3: Standardize team names
        df = self._standardize_team_names(df)
        
        # Step 4: Clean numeric columns
        df = self._clean_numeric_columns(df)
        
        # Step 5: Add derived features
        df = self._add_derived_features(df)
        
        # Step 6: Validate results
        df = self._validate_results(df)
        
        # Log processing summary
        final_count = len(df)
        dropped = original_count - final_count
        if dropped > 0:
            self.logger.info(
                f"Dropped {dropped} invalid rows ({dropped/original_count*100:.1f}%)"
            )
        
        self.logger.info(f"Processing complete: {final_count} valid matches")
        
        return df
    
    def _drop_invalid_rows(self, df: pd.DataFrame) -> pd.DataFrame:
        """Drop rows with missing essential data."""
        essential_cols = ['home_team', 'away_team']
        
        for col in essential_cols:
            if col in df.columns:
                before = len(df)
                df = df[df[col].notna() & (df[col] != '')]
                after = len(df)
                if before != after:
                    self.logger.debug(
                        f"Dropped {before - after} rows with missing {col}"
                    )
        
        return df
    
    def _parse_dates(self, df: pd.DataFrame) -> pd.DataFrame:
        """Parse and validate date columns."""
        if 'date' in df.columns:
            df['match_date'] = df['date'].apply(
                lambda x: parse_date(str(x)) if pd.notna(x) else None
            )
            
            # Drop rows without valid dates
            before = len(df)
            df = df[df['match_date'].notna()]
            after = len(df)
            if before != after:
                self.logger.debug(
                    f"Dropped {before - after} rows with invalid dates"
                )
        
        if 'time' in df.columns:
            df['match_time'] = df['time'].apply(
                lambda x: parse_time(str(x)) if pd.notna(x) else None
            )
        
        return df
    
    def _standardize_team_names(self, df: pd.DataFrame) -> pd.DataFrame:
        """Standardize team names for consistency."""
        for col in ['home_team', 'away_team']:
            if col in df.columns:
                df[col] = df[col].apply(self._get_standardized_name)
        
        return df
    
    def _get_standardized_name(self, name: str) -> str:
        """Get standardized team name with caching."""
        if not name or pd.isna(name):
            return ""
        
        name = str(name)
        
        if name not in self._team_name_cache:
            self._team_name_cache[name] = standardize_team_name(name)
        
        return self._team_name_cache[name]
    
    def _clean_numeric_columns(self, df: pd.DataFrame) -> pd.DataFrame:
        """Clean and convert numeric columns."""
        # Integer columns (goals, cards, etc.)
        int_cols = [
            'fthg', 'ftag', 'hthg', 'htag',
            'hs', 'as', 'hst', 'ast',
            'hf', 'af', 'hc', 'ac',
            'hy', 'ay', 'hr', 'ar'
        ]
        
        for col in int_cols:
            if col in df.columns:
                df[col] = df[col].apply(lambda x: safe_int(x, None))
        
        # Float columns (odds)
        float_cols = [
            'b365h', 'b365d', 'b365a',
            'b365_over25', 'b365_under25',
            'avg_over25', 'avg_under25',
        ]
        
        for col in float_cols:
            if col in df.columns:
                df[col] = df[col].apply(lambda x: safe_float(x, None))
        
        return df
    
    def _add_derived_features(self, df: pd.DataFrame) -> pd.DataFrame:
        """Add derived features to the DataFrame."""
        # Total goals
        if 'fthg' in df.columns and 'ftag' in df.columns:
            df['total_goals'] = df.apply(
                lambda r: r['fthg'] + r['ftag'] 
                if pd.notna(r['fthg']) and pd.notna(r['ftag']) 
                else None,
                axis=1
            )
            
            # Over 2.5 flag
            df['is_over_25'] = df['total_goals'].apply(
                lambda x: x > 2.5 if pd.notna(x) else None
            )
            
            # BTTS flag
            df['is_btts'] = df.apply(
                lambda r: (r['fthg'] > 0 and r['ftag'] > 0)
                if pd.notna(r['fthg']) and pd.notna(r['ftag'])
                else None,
                axis=1
            )
        
        # Season calculation
        if 'match_date' in df.columns:
            df['calculated_season'] = df['match_date'].apply(
                lambda x: calculate_season(x) if pd.notna(x) else None
            )
            
            # Month for seasonal analysis
            df['month'] = df['match_date'].apply(
                lambda x: x.month if pd.notna(x) else None
            )
        
        return df
    
    def _validate_results(self, df: pd.DataFrame) -> pd.DataFrame:
        """Validate match results are consistent."""
        if 'ftr' in df.columns and 'fthg' in df.columns and 'ftag' in df.columns:
            # Check result matches score
            def check_result(row):
                if pd.isna(row['fthg']) or pd.isna(row['ftag']) or pd.isna(row['ftr']):
                    return True  # Can't validate
                
                expected = 'D'
                if row['fthg'] > row['ftag']:
                    expected = 'H'
                elif row['fthg'] < row['ftag']:
                    expected = 'A'
                
                return row['ftr'] == expected
            
            inconsistent = ~df.apply(check_result, axis=1)
            if inconsistent.any():
                count = inconsistent.sum()
                self.logger.warning(
                    f"{count} matches have inconsistent FTR vs score"
                )
                self._warnings.append(f"Inconsistent FTR: {count} matches")
        
        return df
    
    def convert_to_matches(self, df: pd.DataFrame) -> list[Match]:
        """
        Convert DataFrame rows to Match entities.
        
        Args:
            df: Processed DataFrame
            
        Returns:
            List of Match entities
        """
        matches = []
        errors = 0
        
        for _, row in df.iterrows():
            try:
                match = self._row_to_match(row)
                if match:
                    matches.append(match)
            except Exception as e:
                errors += 1
                if errors <= 5:  # Only log first 5 errors
                    self.logger.debug(f"Error converting row: {e}")
        
        if errors > 0:
            self.logger.warning(f"Failed to convert {errors} rows to Match entities")
        
        self.logger.info(f"Converted {len(matches)} matches")
        return matches
    
    def _row_to_match(self, row: pd.Series) -> Optional[Match]:
        """
        Convert a DataFrame row to a Match entity.
        
        Args:
            row: DataFrame row
            
        Returns:
            Match entity or None if conversion failed
        """
        # Required fields
        home_team = row.get('home_team')
        away_team = row.get('away_team')
        match_date = row.get('match_date')
        
        if not all([home_team, away_team, match_date]):
            return None
        
        # Get league and season
        league = row.get('league', 'UNK')
        season = row.get('season') or row.get('calculated_season', 'Unknown')
        
        # Parse time if string
        match_time = row.get('match_time')
        if match_time and isinstance(match_time, str):
            parts = match_time.split(':')
            if len(parts) >= 2:
                match_time = time(int(parts[0]), int(parts[1]))
            else:
                match_time = None
        
        # Create Match entity
        match = Match(
            id=str(uuid.uuid4()),
            home_team=str(home_team),
            away_team=str(away_team),
            match_date=match_date if isinstance(match_date, date) else None,
            match_time=match_time if isinstance(match_time, time) else None,
            league=str(league),
            season=str(season),
            
            # Full-time results
            fthg=safe_int(row.get('fthg'), None),
            ftag=safe_int(row.get('ftag'), None),
            ftr=row.get('ftr') if pd.notna(row.get('ftr')) else None,
            
            # Half-time results
            hthg=safe_int(row.get('hthg'), None),
            htag=safe_int(row.get('htag'), None),
            htr=row.get('htr') if pd.notna(row.get('htr')) else None,
            
            # Statistics
            hs=safe_int(row.get('hs'), None),
            as_=safe_int(row.get('as'), None),
            hst=safe_int(row.get('hst'), None),
            ast=safe_int(row.get('ast'), None),
            hf=safe_int(row.get('hf'), None),
            af=safe_int(row.get('af'), None),
            hc=safe_int(row.get('hc'), None),
            ac=safe_int(row.get('ac'), None),
            hy=safe_int(row.get('hy'), None),
            ay=safe_int(row.get('ay'), None),
            hr=safe_int(row.get('hr'), None),
            ar=safe_int(row.get('ar'), None),
            
            # Other
            referee=row.get('referee') if pd.notna(row.get('referee')) else None,
            
            # Odds - 1X2
            b365h=safe_float(row.get('b365h'), None),
            b365d=safe_float(row.get('b365d'), None),
            b365a=safe_float(row.get('b365a'), None),
            
            # Odds - Over/Under 2.5
            b365_over25=safe_float(row.get('b365_over25'), None),
            b365_under25=safe_float(row.get('b365_under25'), None),
        )
        
        return match
    
    def get_processing_warnings(self) -> list[str]:
        """Get list of warnings generated during processing."""
        return self._warnings.copy()
    
    def clear_warnings(self) -> None:
        """Clear accumulated warnings."""
        self._warnings.clear()
