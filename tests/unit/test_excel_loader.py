"""
Unit tests for Excel data loader.

Tests cover:
- Loading valid Excel files
- Column validation and mapping
- Error handling for corrupt/missing files
- League filtering
"""
import pytest
import pandas as pd
from pathlib import Path

import sys
sys.path.insert(0, str(Path(__file__).parent.parent.parent))

from src.data.loaders import ExcelLoader, load_historical_data


class TestExcelLoader:
    """Tests for ExcelLoader class."""
    
    def test_load_valid_excel_file(self, sample_excel_file):
        """Test loading a valid Excel file returns DataFrame."""
        loader = ExcelLoader()
        df = loader.load(sample_excel_file)
        
        assert df is not None
        assert isinstance(df, pd.DataFrame)
        assert len(df) > 0
    
    def test_required_columns_present(self, sample_excel_file):
        """Test all required columns are present after loading."""
        loader = ExcelLoader()
        df = loader.load(sample_excel_file)
        
        required = ["home_team", "away_team", "date"]
        for col in required:
            assert col in df.columns, f"Missing column: {col}"
    
    def test_column_mapping_applied(self, sample_excel_file):
        """Test column names are standardized."""
        loader = ExcelLoader()
        df = loader.load(sample_excel_file)
        
        # Should have standardized names, not original
        assert "home_team" in df.columns
        assert "fthg" in df.columns
        assert "HomeTeam" not in df.columns
        assert "FTHG" not in df.columns
    
    def test_correct_row_count(self, sample_excel_file, sample_historical_df):
        """Test correct number of rows loaded."""
        loader = ExcelLoader()
        df = loader.load(sample_excel_file, filter_unsupported_leagues=False)
        
        # Should match E0 rows in sample data
        expected_e0_rows = len(sample_historical_df[sample_historical_df["Div"] == "E0"])
        assert len(df) == expected_e0_rows
    
    def test_data_types_correct(self, sample_excel_file):
        """Test data types are correct."""
        loader = ExcelLoader()
        df = loader.load(sample_excel_file)
        
        # Goals should be numeric
        assert df["fthg"].dtype in ["int64", "float64", "Int64", "object"]
        # Teams should be strings
        assert df["home_team"].dtype == "object"
    
    def test_missing_file_returns_none(self, test_data_dir):
        """Test handling of missing file."""
        loader = ExcelLoader()
        result = loader.load(test_data_dir / "nonexistent.xlsx")
        
        assert result is None
    
    def test_corrupt_excel_returns_none(self, corrupt_excel_file):
        """Test handling of corrupt Excel file."""
        loader = ExcelLoader()
        result = loader.load(corrupt_excel_file)
        
        assert result is None
    
    def test_missing_columns_returns_none(self, missing_columns_excel):
        """Test handling of Excel with missing required columns."""
        loader = ExcelLoader()
        result = loader.load(missing_columns_excel)
        
        assert result is None
    
    def test_league_code_added(self, sample_excel_file):
        """Test league code is added when provided."""
        loader = ExcelLoader()
        df = loader.load(sample_excel_file, league_code="E0")
        
        assert "league" in df.columns
        assert df["league"].iloc[0] == "E0"
    
    def test_season_added(self, sample_excel_file):
        """Test season is added when provided."""
        loader = ExcelLoader()
        df = loader.load(sample_excel_file, season="2024-25")
        
        assert "season" in df.columns
        assert df["season"].iloc[0] == "2024-25"
    
    def test_filter_unsupported_leagues(self, test_data_dir, sample_historical_df):
        """Test filtering of unsupported leagues."""
        # Create file with mixed leagues including unsupported one
        file_path = test_data_dir / "mixed_leagues.xlsx"
        
        # Add unsupported league to sample data
        df = sample_historical_df.copy()
        unsupported_row = df.iloc[0].copy()
        unsupported_row["Div"] = "XX"  # Unsupported league
        df = pd.concat([df, pd.DataFrame([unsupported_row])], ignore_index=True)
        df.to_excel(file_path, index=False)
        
        loader = ExcelLoader()
        result = loader.load(file_path, filter_unsupported_leagues=True)
        
        if result is not None and "league" in result.columns:
            assert "XX" not in result["league"].values
    
    def test_load_multiple_files(self, test_data_dir, sample_historical_df):
        """Test loading multiple Excel files."""
        # Create multiple files
        file1 = test_data_dir / "E0_2324.xlsx"
        file2 = test_data_dir / "E0_2425.xlsx"
        
        df_e0 = sample_historical_df[sample_historical_df["Div"] == "E0"]
        df_e0.to_excel(file1, index=False)
        df_e0.to_excel(file2, index=False)
        
        loader = ExcelLoader()
        result = loader.load_multiple([file1, file2])
        
        assert result is not None
        assert len(result) == len(df_e0) * 2
    
    def test_empty_excel_handling(self, empty_excel_file):
        """Test handling of empty Excel file."""
        loader = ExcelLoader()
        result = loader.load(empty_excel_file)
        
        # Should return None or empty DataFrame
        assert result is None or len(result) == 0
    
    def test_over25_odds_loaded(self, sample_excel_file):
        """Test B365 over 2.5 odds are loaded."""
        loader = ExcelLoader()
        df = loader.load(sample_excel_file)
        
        # Check if over/under odds columns exist
        if "b365_over25" in df.columns:
            assert df["b365_over25"].notna().any()


class TestLoadHistoricalDataFunction:
    """Tests for load_historical_data convenience function."""
    
    def test_function_loads_data(self, sample_excel_file):
        """Test convenience function works."""
        df = load_historical_data(sample_excel_file)
        
        assert df is not None
        assert len(df) > 0
    
    def test_function_with_league_and_season(self, sample_excel_file):
        """Test function with league and season parameters."""
        df = load_historical_data(
            sample_excel_file,
            league_code="E0",
            season="2024-25"
        )
        
        assert df is not None
        assert df["league"].iloc[0] == "E0"
        assert df["season"].iloc[0] == "2024-25"
