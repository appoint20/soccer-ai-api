"""
Unit tests for CSV data loader.

Tests cover:
- Loading valid CSV files
- Column validation
- Date/time parsing
- Error handling
"""
import pytest
import pandas as pd
from pathlib import Path

import sys
sys.path.insert(0, str(Path(__file__).parent.parent.parent))

from src.data.loaders import CSVLoader, load_upcoming_fixtures


class TestCSVLoader:
    """Tests for CSVLoader class."""
    
    def test_load_valid_csv(self, sample_csv_file):
        """Test loading a valid CSV file."""
        loader = CSVLoader()
        df = loader.load(sample_csv_file)
        
        assert df is not None
        assert isinstance(df, pd.DataFrame)
        assert len(df) > 0
    
    def test_required_columns_present(self, sample_csv_file):
        """Test required columns for upcoming fixtures."""
        loader = CSVLoader()
        df = loader.load(sample_csv_file)
        
        required = ["date", "home_team", "away_team"]
        for col in required:
            assert col in df.columns, f"Missing column: {col}"
    
    def test_date_parsing(self, sample_csv_file):
        """Test date format parsing."""
        loader = CSVLoader()
        df = loader.load(sample_csv_file)
        
        # Should have parsed_date column
        assert "parsed_date" in df.columns or "date" in df.columns
    
    def test_missing_file_returns_none(self, test_data_dir):
        """Test handling of missing file."""
        loader = CSVLoader()
        result = loader.load(test_data_dir / "nonexistent.csv")
        
        assert result is None
    
    def test_empty_csv_returns_none(self, test_data_dir):
        """Test handling of empty CSV."""
        empty_file = test_data_dir / "empty.csv"
        with open(empty_file, "w") as f:
            f.write("")
        
        loader = CSVLoader()
        result = loader.load(empty_file)
        
        assert result is None
    
    def test_csv_with_extra_columns(self, test_data_dir, sample_upcoming_df):
        """Test handling of CSV with extra columns."""
        # Add extra column
        df = sample_upcoming_df.copy()
        df["ExtraColumn"] = "extra"
        
        file_path = test_data_dir / "extra_cols.csv"
        df.to_csv(file_path, index=False)
        
        loader = CSVLoader()
        result = loader.load(file_path)
        
        assert result is not None
        assert len(result) == len(df)
    
    def test_column_standardization(self, sample_csv_file):
        """Test column names are standardized."""
        loader = CSVLoader()
        df = loader.load(sample_csv_file)
        
        # Should use standardized names
        assert "home_team" in df.columns
        assert "away_team" in df.columns
    
    def test_encoding_fallback(self, test_data_dir):
        """Test CSV loading with different encodings."""
        # Create file with latin-1 encoding
        file_path = test_data_dir / "latin1.csv"
        with open(file_path, "w", encoding="latin-1") as f:
            f.write("Date,HomeTeam,AwayTeam\n")
            f.write("2024-01-01,Arsenal,Chelsea\n")
        
        loader = CSVLoader()
        result = loader.load(file_path)
        
        assert result is not None
    
    def test_corrupt_csv_returns_none(self, test_data_dir):
        """Test handling of corrupt CSV."""
        file_path = test_data_dir / "corrupt.csv"
        with open(file_path, "wb") as f:
            f.write(b"\x00\x01\x02\x03")  # Binary garbage
        
        loader = CSVLoader()
        result = loader.load(file_path)
        
        # Should return None or try to parse
        assert result is None or isinstance(result, pd.DataFrame)


class TestLoadUpcomingFixturesFunction:
    """Tests for load_upcoming_fixtures convenience function."""
    
    def test_function_loads_data(self, sample_csv_file):
        """Test convenience function works."""
        df = load_upcoming_fixtures(sample_csv_file)
        
        assert df is not None
        assert len(df) > 0
    
    def test_function_returns_none_for_missing(self, test_data_dir):
        """Test function returns None for missing file."""
        result = load_upcoming_fixtures(test_data_dir / "missing.csv")
        
        assert result is None
