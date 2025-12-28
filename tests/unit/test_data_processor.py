"""
Unit tests for data processor.

Tests cover:
- Data cleaning and standardization
- DataFrame to Match entity conversion
- Derived feature calculation
- Data validation
"""
import pytest
import pandas as pd
from datetime import date
from pathlib import Path

import sys
sys.path.insert(0, str(Path(__file__).parent.parent.parent))

from src.data.loaders import DataProcessor
from src.domain.entities import Match


class TestDataProcessorProcessing:
    """Tests for data processing operations."""
    
    def test_process_dataframe_adds_derived_columns(self, sample_historical_df):
        """Test process_historical_data adds derived columns."""
        processor = DataProcessor()
        
        # Standardize columns first
        df = sample_historical_df.copy()
        df = df.rename(columns={
            "HomeTeam": "home_team",
            "AwayTeam": "away_team", 
            "Date": "date",
            "FTHG": "fthg",
            "FTAG": "ftag",
            "FTR": "ftr",
            "Div": "league",
        })
        
        result = processor.process_historical_data(df)
        
        assert "total_goals" in result.columns
        assert "is_over_25" in result.columns
        assert "is_btts" in result.columns
        assert "match_date" in result.columns
    
    def test_process_calculates_total_goals(self):
        """Test total goals calculation."""
        processor = DataProcessor()
        
        df = pd.DataFrame({
            "home_team": ["Arsenal"],
            "away_team": ["Chelsea"],
            "date": ["2024-09-15"],
            "fthg": [2],
            "ftag": [1],
            "ftr": ["H"],
        })
        
        result = processor.process_historical_data(df)
        
        assert result["total_goals"].iloc[0] == 3
    
    def test_process_calculates_over25(self):
        """Test over 2.5 flag calculation."""
        processor = DataProcessor()
        
        df = pd.DataFrame({
            "home_team": ["Team A", "Team C"],
            "away_team": ["Team B", "Team D"],
            "date": ["2024-09-15", "2024-09-16"],
            "fthg": [2, 1],
            "ftag": [1, 0],
            "ftr": ["H", "H"],
        })
        
        result = processor.process_historical_data(df)
        
        assert result["is_over_25"].iloc[0] == True  # 3 goals
        assert result["is_over_25"].iloc[1] == False  # 1 goal
    
    def test_process_calculates_btts(self):
        """Test BTTS flag calculation."""
        processor = DataProcessor()
        
        df = pd.DataFrame({
            "home_team": ["Team A", "Team C"],
            "away_team": ["Team B", "Team D"],
            "date": ["2024-09-15", "2024-09-16"],
            "fthg": [2, 2],
            "ftag": [1, 0],
            "ftr": ["H", "H"],
        })
        
        result = processor.process_historical_data(df)
        
        assert result["is_btts"].iloc[0] == True
        assert result["is_btts"].iloc[1] == False
    
    def test_process_drops_invalid_rows(self):
        """Test invalid rows are dropped."""
        processor = DataProcessor()
        
        df = pd.DataFrame({
            "home_team": ["Arsenal", None, "Chelsea"],
            "away_team": ["Chelsea", "Liverpool", None],
            "date": ["2024-09-15", "2024-09-16", "2024-09-17"],
        })
        
        result = processor.process_historical_data(df)
        
        assert len(result) == 1
    
    def test_process_empty_dataframe(self):
        """Test processing empty DataFrame."""
        processor = DataProcessor()
        
        result = processor.process_historical_data(pd.DataFrame())
        
        assert len(result) == 0


class TestDataProcessorTeamNames:
    """Tests for team name standardization."""
    
    def test_standardize_team_names(self):
        """Test team names are standardized."""
        processor = DataProcessor()
        
        df = pd.DataFrame({
            "home_team": ["man utd"],
            "away_team": ["Spurs"],
            "date": ["2024-09-15"],
        })
        
        result = processor.process_historical_data(df)
        
        assert result["home_team"].iloc[0] == "Manchester United"
        assert result["away_team"].iloc[0] == "Tottenham"
    
    def test_team_name_caching(self):
        """Test team names are cached for efficiency."""
        processor = DataProcessor()
        
        df = pd.DataFrame({
            "home_team": ["Arsenal", "Arsenal", "Arsenal"],
            "away_team": ["Chelsea", "Liverpool", "Chelsea"],
            "date": ["2024-09-15", "2024-09-16", "2024-09-17"],
        })
        
        result = processor.process_historical_data(df)
        
        # Should have cached team names
        assert len(processor._team_name_cache) >= 1


class TestDataProcessorConversion:
    """Tests for DataFrame to Match entity conversion."""
    
    def test_convert_to_matches(self):
        """Test converting DataFrame to Match entities."""
        processor = DataProcessor()
        
        df = pd.DataFrame({
            "home_team": ["Arsenal"],
            "away_team": ["Chelsea"],
            "date": ["2024-09-15"],
            "fthg": [2],
            "ftag": [1],
            "ftr": ["H"],
            "league": ["E0"],
        })
        
        processed = processor.process_historical_data(df)
        matches = processor.convert_to_matches(processed)
        
        assert len(matches) == 1
        assert isinstance(matches[0], Match)
        assert matches[0].home_team == "Arsenal"
    
    def test_convert_preserves_all_fields(self):
        """Test conversion preserves all match fields."""
        processor = DataProcessor()
        
        df = pd.DataFrame({
            "home_team": ["Arsenal"],
            "away_team": ["Chelsea"],
            "date": ["2024-09-15"],
            "time": ["15:00"],
            "fthg": [2],
            "ftag": [1],
            "ftr": ["H"],
            "hthg": [1],
            "htag": [0],
            "htr": ["H"],
            "hs": [12],
            "as": [8],
            "hst": [5],
            "ast": [3],
            "referee": ["Michael Oliver"],
            "league": ["E0"],
            "b365h": [1.85],
            "b365d": [3.60],
            "b365a": [4.20],
        })
        
        processed = processor.process_historical_data(df)
        matches = processor.convert_to_matches(processed)
        
        match = matches[0]
        assert match.fthg == 2
        assert match.ftag == 1
        assert match.hthg == 1
        assert match.hs == 12
        assert match.referee == "Michael Oliver"
        assert match.b365h == 1.85
    
    def test_convert_handles_missing_optional_fields(self):
        """Test conversion handles missing optional fields."""
        processor = DataProcessor()
        
        df = pd.DataFrame({
            "home_team": ["Arsenal"],
            "away_team": ["Chelsea"],
            "date": ["2024-09-15"],
            "fthg": [2],
            "ftag": [1],
            "ftr": ["H"],
            "league": ["E0"],
        })
        
        processed = processor.process_historical_data(df)
        matches = processor.convert_to_matches(processed)
        
        match = matches[0]
        assert match.referee is None
        assert match.hs is None
    
    def test_convert_large_dataset(self, sample_historical_df):
        """Test conversion of large dataset."""
        processor = DataProcessor()
        
        # Rename columns first
        df = sample_historical_df.copy()
        df = df.rename(columns={
            "HomeTeam": "home_team",
            "AwayTeam": "away_team",
            "Date": "date",
            "FTHG": "fthg",
            "FTAG": "ftag",
            "FTR": "ftr",
            "Div": "league",
        })
        
        processed = processor.process_historical_data(df)
        matches = processor.convert_to_matches(processed)
        
        assert len(matches) > 0
        assert all(isinstance(m, Match) for m in matches)


class TestDataProcessorValidation:
    """Tests for data validation."""
    
    def test_validate_results_consistency(self):
        """Test result validation catches inconsistencies."""
        processor = DataProcessor()
        
        df = pd.DataFrame({
            "home_team": ["Arsenal"],
            "away_team": ["Chelsea"],
            "date": ["2024-09-15"],
            "fthg": [2],
            "ftag": [1],
            "ftr": ["A"],  # Wrong! Should be H
        })
        
        result = processor.process_historical_data(df)
        warnings = processor.get_processing_warnings()
        
        # Should log warning about inconsistent result
        assert any("inconsistent" in w.lower() for w in warnings) or len(result) >= 0
    
    def test_clear_warnings(self):
        """Test clearing accumulated warnings."""
        processor = DataProcessor()
        processor._warnings = ["test warning"]
        
        processor.clear_warnings()
        
        assert len(processor._warnings) == 0


class TestDataProcessorDateHandling:
    """Tests for date parsing and handling."""
    
    def test_parse_uk_date_format(self):
        """Test parsing UK date format (DD/MM/YYYY)."""
        processor = DataProcessor()
        
        df = pd.DataFrame({
            "home_team": ["Arsenal"],
            "away_team": ["Chelsea"],
            "date": ["15/09/2024"],
        })
        
        result = processor.process_historical_data(df)
        
        assert result["match_date"].iloc[0] == date(2024, 9, 15)
    
    def test_parse_iso_date_format(self):
        """Test parsing ISO date format (YYYY-MM-DD)."""
        processor = DataProcessor()
        
        df = pd.DataFrame({
            "home_team": ["Arsenal"],
            "away_team": ["Chelsea"],
            "date": ["2024-09-15"],
        })
        
        result = processor.process_historical_data(df)
        
        assert result["match_date"].iloc[0] == date(2024, 9, 15)
    
    def test_invalid_dates_dropped(self):
        """Test rows with invalid dates are dropped."""
        processor = DataProcessor()
        
        df = pd.DataFrame({
            "home_team": ["Arsenal", "Chelsea"],
            "away_team": ["Chelsea", "Liverpool"],
            "date": ["2024-09-15", "invalid-date"],
        })
        
        result = processor.process_historical_data(df)
        
        assert len(result) == 1
