"""Tests for data loading functionality."""
import pytest
import sys
from pathlib import Path
from datetime import date, time
from io import StringIO
import tempfile
import json

# Add project root to path
project_root = Path(__file__).parent.parent
sys.path.insert(0, str(project_root))

import pandas as pd

from src.domain.entities import Match, Team, Prediction
from src.data.storage import JSONStorage
from src.data.loaders import DataProcessor
from src.utils.helpers import (
    standardize_team_name,
    calculate_season,
    get_season_of_year,
    validate_league_code,
    parse_date,
    parse_time,
)


class TestMatchEntity:
    """Tests for Match entity."""
    
    def test_create_match(self):
        """Test creating a basic match."""
        match = Match(
            home_team="Arsenal",
            away_team="Chelsea",
            match_date=date(2024, 1, 15),
            league="E0",
            season="2023-24",
        )
        
        assert match.home_team == "Arsenal"
        assert match.away_team == "Chelsea"
        assert match.league == "E0"
        assert not match.is_completed
    
    def test_match_with_results(self):
        """Test match with results."""
        match = Match(
            home_team="Arsenal",
            away_team="Chelsea",
            match_date=date(2024, 1, 15),
            league="E0",
            season="2023-24",
            fthg=2,
            ftag=1,
            ftr="H",
        )
        
        assert match.is_completed
        assert match.total_goals == 3
        assert match.is_over_25 is True
        assert match.is_btts is True
    
    def test_match_serialization(self):
        """Test match to_dict and from_dict."""
        match = Match(
            home_team="Man United",
            away_team="Liverpool",
            match_date=date(2024, 3, 10),
            match_time=time(15, 0),
            league="E0",
            season="2023-24",
            fthg=0,
            ftag=0,
            ftr="D",
        )
        
        data = match.to_dict()
        assert data["home_team"] == "Man United"
        assert data["match_date"] == "2024-03-10"
        assert data["match_time"] == "15:00"
        
        # Recreate from dict
        match2 = Match.from_dict(data)
        assert match2.home_team == match.home_team
        assert match2.match_date == match.match_date
    
    def test_match_key(self):
        """Test unique match key generation."""
        match = Match(
            home_team="Arsenal",
            away_team="Chelsea",
            match_date=date(2024, 1, 15),
            league="E0",
            season="2023-24",
        )
        
        key = match.match_key
        assert "2024-01-15" in key
        assert "Arsenal" in key
        assert "Chelsea" in key


class TestTeamEntity:
    """Tests for Team entity."""
    
    def test_create_team(self):
        """Test creating a team."""
        team = Team(
            name="Arsenal",
            league="E0",
        )
        
        assert team.name == "Arsenal"
        assert team.stats.total_matches == 0
    
    def test_team_form(self):
        """Test team form tracking."""
        team = Team(name="Arsenal", league="E0")
        
        team.add_result("W")
        team.add_result("W")
        team.add_result("D")
        team.add_result("L")
        team.add_result("W")
        
        assert team.form_string == "WWDLW"
        assert team.form_points == 10  # 3+3+1+0+3


class TestPredictionEntity:
    """Tests for Prediction entity."""
    
    def test_create_prediction(self):
        """Test creating a prediction."""
        pred = Prediction(
            match_id="test-123",
            over25_prediction=True,
            over25_probability=0.75,
            btts_prediction=True,
            btts_probability=0.65,
        )
        
        assert pred.match_id == "test-123"
        assert pred.over25_prediction is True
        assert not pred.is_verified
    
    def test_prediction_verification(self):
        """Test prediction verification."""
        pred = Prediction(
            match_id="test-123",
            over25_prediction=True,
            over25_probability=0.75,
            btts_prediction=False,
            btts_probability=0.40,
        )
        
        pred.set_actual_results(over25=True, btts=True, result="H")
        
        assert pred.is_verified
        assert pred.over25_correct is True
        assert pred.btts_correct is False
    
    def test_confidence_calculation(self):
        """Test confidence level calculation."""
        assert Prediction.calculate_confidence(0.75) == "high"
        assert Prediction.calculate_confidence(0.60) == "medium"
        assert Prediction.calculate_confidence(0.45) == "low"


class TestJSONStorage:
    """Tests for JSON storage operations."""
    
    def test_save_and_load(self):
        """Test saving and loading JSON."""
        storage = JSONStorage()
        
        with tempfile.TemporaryDirectory() as tmpdir:
            filepath = Path(tmpdir) / "test.json"
            
            data = {"name": "test", "values": [1, 2, 3]}
            
            # Save
            result = storage.save(data, filepath)
            assert result is True
            assert filepath.exists()
            
            # Load
            loaded = storage.load(filepath)
            assert loaded == data
    
    def test_append_to_list(self):
        """Test appending to a JSON list."""
        storage = JSONStorage()
        
        with tempfile.TemporaryDirectory() as tmpdir:
            filepath = Path(tmpdir) / "list.json"
            
            # Save initial list
            storage.save([1, 2, 3], filepath)
            
            # Append
            storage.append([4, 5], filepath)
            
            loaded = storage.load(filepath)
            assert loaded == [1, 2, 3, 4, 5]
    
    def test_append_to_dict(self):
        """Test appending to a JSON dict."""
        storage = JSONStorage()
        
        with tempfile.TemporaryDirectory() as tmpdir:
            filepath = Path(tmpdir) / "dict.json"
            
            # Save initial dict
            storage.save({"a": 1}, filepath)
            
            # Append/merge
            storage.append({"b": 2}, filepath)
            
            loaded = storage.load(filepath)
            assert loaded == {"a": 1, "b": 2}
    
    def test_backup(self):
        """Test backup creation."""
        storage = JSONStorage()
        
        with tempfile.TemporaryDirectory() as tmpdir:
            filepath = Path(tmpdir) / "data.json"
            
            # Save file
            storage.save({"data": "original"}, filepath)
            
            # Create backup
            backup_path = storage.create_backup(filepath)
            
            assert backup_path is not None
            assert backup_path.exists()
            assert "data_" in backup_path.name
    
    def test_load_missing_file(self):
        """Test loading non-existent file returns default."""
        storage = JSONStorage()
        
        result = storage.load("/nonexistent/path.json", default=[])
        assert result == []


class TestHelperFunctions:
    """Tests for utility helper functions."""
    
    def test_standardize_team_name(self):
        """Test team name standardization."""
        assert standardize_team_name("man utd") == "Manchester United"
        assert standardize_team_name("Man City") == "Manchester City"
        assert standardize_team_name("  Arsenal  ") == "Arsenal"
    
    def test_calculate_season(self):
        """Test season calculation."""
        # August = start of new season
        assert calculate_season(date(2024, 8, 15)) == "2024-25"
        
        # January = still previous season
        assert calculate_season(date(2025, 1, 15)) == "2024-25"
        
        # May = end of season
        assert calculate_season(date(2025, 5, 20)) == "2024-25"
    
    def test_get_season_of_year(self):
        """Test meteorological season detection."""
        assert get_season_of_year(date(2024, 1, 15)) == "Winter"
        assert get_season_of_year(date(2024, 4, 15)) == "Spring"
        assert get_season_of_year(date(2024, 7, 15)) == "Summer"
        assert get_season_of_year(date(2024, 10, 15)) == "Autumn"
    
    def test_validate_league_code(self):
        """Test league code validation."""
        assert validate_league_code("E0") is True
        assert validate_league_code("D1") is True
        assert validate_league_code("XX") is False
    
    def test_parse_date(self):
        """Test date parsing."""
        assert parse_date("2024-01-15") == date(2024, 1, 15)
        assert parse_date("15/01/2024") == date(2024, 1, 15)
        assert parse_date("invalid") is None
    
    def test_parse_time(self):
        """Test time parsing."""
        assert parse_time("15:30") == "15:30"
        assert parse_time("9:00") == "09:00"
        assert parse_time("") is None


class TestDataProcessor:
    """Tests for data processing."""
    
    def test_process_dataframe(self):
        """Test processing a DataFrame."""
        processor = DataProcessor()
        
        # Create sample data
        data = {
            "date": ["2024-01-15", "2024-01-16"],
            "home_team": ["Arsenal", "Chelsea"],
            "away_team": ["Chelsea", "Man Utd"],
            "fthg": [2, 1],
            "ftag": [1, 1],
            "ftr": ["H", "D"],
            "league": ["E0", "E0"],
        }
        df = pd.DataFrame(data)
        
        processed = processor.process_historical_data(df)
        
        assert len(processed) == 2
        assert "total_goals" in processed.columns
        assert "is_over_25" in processed.columns
        assert "match_date" in processed.columns
    
    def test_convert_to_matches(self):
        """Test converting DataFrame to Match entities."""
        processor = DataProcessor()
        
        data = {
            "date": ["2024-01-15"],
            "home_team": ["Arsenal"],
            "away_team": ["Chelsea"],
            "fthg": [2],
            "ftag": [0],
            "ftr": ["H"],
            "league": ["E0"],
        }
        df = pd.DataFrame(data)
        
        processed = processor.process_historical_data(df)
        matches = processor.convert_to_matches(processed)
        
        assert len(matches) == 1
        assert matches[0].home_team == "Arsenal"
        assert matches[0].total_goals == 2


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
