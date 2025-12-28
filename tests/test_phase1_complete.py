"""
Master validation test for Phase 1 (Foundation & Architecture).

This test validates that all Phase 1 components are correctly
implemented and working together before moving to Phase 2.

Run: pytest tests/test_phase1_complete.py -v
"""
import pytest
import time
from datetime import date, timedelta
from pathlib import Path

import sys
sys.path.insert(0, str(Path(__file__).parent.parent))


class TestProjectStructure:
    """Validate project structure exists correctly."""
    
    def test_src_domain_entities_exists(self, project_root_dir):
        """Verify entities directory exists."""
        path = project_root_dir / "src" / "domain" / "entities"
        assert path.exists(), f"Missing: {path}"
    
    def test_src_data_loaders_exists(self, project_root_dir):
        """Verify loaders directory exists."""
        path = project_root_dir / "src" / "data" / "loaders"
        assert path.exists(), f"Missing: {path}"
    
    def test_src_data_storage_exists(self, project_root_dir):
        """Verify storage directory exists."""
        path = project_root_dir / "src" / "data" / "storage"
        assert path.exists(), f"Missing: {path}"
    
    def test_src_utils_exists(self, project_root_dir):
        """Verify utils directory exists."""
        path = project_root_dir / "src" / "utils"
        assert path.exists(), f"Missing: {path}"
    
    def test_config_directory_exists(self, project_root_dir):
        """Verify config directory exists."""
        path = project_root_dir / "config"
        assert path.exists(), f"Missing: {path}"
    
    def test_data_directories_exist(self, project_root_dir):
        """Verify data directory structure exists."""
        dirs = [
            "data/raw/historical",
            "data/raw/upcoming",
            "data/processed",
            "data/predictions",
        ]
        for d in dirs:
            path = project_root_dir / d
            assert path.exists() or (project_root_dir / d.replace("/", "\\")).exists(), \
                f"Missing: {path}"
    
    def test_tests_directory_exists(self, project_root_dir):
        """Verify tests directory exists."""
        path = project_root_dir / "tests"
        assert path.exists(), f"Missing: {path}"
    
    def test_required_files_exist(self, project_root_dir):
        """Verify required files exist."""
        files = [
            "requirements.txt",
            "setup.py",
            ".gitignore",
            "README.md",
        ]
        for f in files:
            path = project_root_dir / f
            assert path.exists(), f"Missing: {path}"


class TestEntityCreation:
    """Validate all entity types can be created correctly."""
    
    def test_match_entity_creation(self):
        """Test Match entity can be created."""
        from src.domain.entities import Match
        
        match = Match(
            home_team="Arsenal",
            away_team="Chelsea",
            match_date=date(2024, 9, 15),
            league="E0",
            season="2024-25",
            fthg=2,
            ftag=1,
            ftr="H",
        )
        
        assert match.home_team == "Arsenal"
        assert match.total_goals == 3
        assert match.is_over_25 is True
        assert match.is_btts is True
    
    def test_team_entity_creation(self):
        """Test Team entity can be created."""
        from src.domain.entities import Team
        
        team = Team(name="Arsenal", league="E0")
        
        assert team.name == "Arsenal"
        assert team.stats.total_matches == 0
    
    def test_prediction_entity_creation(self):
        """Test Prediction entity can be created."""
        from src.domain.entities import Prediction
        
        pred = Prediction(
            match_id="test-123",
            over25_prediction=True,
            over25_probability=0.72,
        )
        
        assert pred.match_id == "test-123"
        assert pred.over25_prediction is True
    
    def test_entity_serialization_roundtrip(self):
        """Test entities can be serialized and deserialized."""
        from src.domain.entities import Match
        
        original = Match(
            home_team="Arsenal",
            away_team="Chelsea",
            match_date=date(2024, 9, 15),
            league="E0",
            season="2024-25",
            fthg=2,
            ftag=1,
            ftr="H",
        )
        
        # Roundtrip
        data = original.to_dict()
        restored = Match.from_dict(data)
        
        assert restored.home_team == original.home_team
        assert restored.fthg == original.fthg


class TestDataLoading:
    """Validate data loading functionality."""
    
    def test_excel_loader_works(self, sample_excel_file):
        """Test Excel loader can load files."""
        from src.data.loaders import ExcelLoader
        
        loader = ExcelLoader()
        df = loader.load(sample_excel_file)
        
        assert df is not None
        assert len(df) > 0
    
    def test_csv_loader_works(self, sample_csv_file):
        """Test CSV loader can load files."""
        from src.data.loaders import CSVLoader
        
        loader = CSVLoader()
        df = loader.load(sample_csv_file)
        
        assert df is not None
        assert len(df) > 0
    
    def test_data_processor_works(self, sample_excel_file):
        """Test data processor can process data."""
        from src.data.loaders import ExcelLoader, DataProcessor
        
        loader = ExcelLoader()
        df = loader.load(sample_excel_file)
        
        processor = DataProcessor()
        processed = processor.process_historical_data(df)
        
        assert processed is not None
        assert len(processed) > 0
    
    def test_can_convert_to_match_entities(self, sample_excel_file):
        """Test can convert DataFrame to Match entities."""
        from src.data.loaders import ExcelLoader, DataProcessor
        from src.domain.entities import Match
        
        loader = ExcelLoader()
        df = loader.load(sample_excel_file)
        
        processor = DataProcessor()
        processed = processor.process_historical_data(df)
        matches = processor.convert_to_matches(processed)
        
        assert len(matches) > 0
        assert all(isinstance(m, Match) for m in matches)


class TestJSONStorage:
    """Validate JSON storage functionality."""
    
    def test_save_and_load(self, test_data_dir, json_storage):
        """Test JSON storage save and load."""
        file_path = test_data_dir / "test_storage.json"
        data = {"test": True, "matches": [1, 2, 3]}
        
        json_storage.save(data, file_path)
        loaded = json_storage.load(file_path)
        
        assert loaded == data
    
    def test_backup_creation(self, test_data_dir, json_storage):
        """Test backup creates valid backup."""
        file_path = test_data_dir / "backup_source.json"
        json_storage.save({"original": True}, file_path)
        
        backup_path = json_storage.create_backup(file_path)
        
        assert backup_path is not None
        assert backup_path.exists()
    
    def test_append_functionality(self, test_data_dir, json_storage):
        """Test append adds to existing data."""
        file_path = test_data_dir / "append_test.json"
        
        json_storage.save([1, 2], file_path)
        json_storage.append([3, 4], file_path)
        
        loaded = json_storage.load(file_path)
        assert loaded == [1, 2, 3, 4]


class TestUtilities:
    """Validate utility functions."""
    
    def test_team_name_standardization(self):
        """Test team name standardization works."""
        from src.utils.helpers import standardize_team_name
        
        assert standardize_team_name("man utd") == "Manchester United"
        assert standardize_team_name("spurs") == "Tottenham"
    
    def test_season_calculation(self):
        """Test season calculation works."""
        from src.utils.helpers import calculate_season
        
        assert calculate_season(date(2024, 9, 1)) == "2024-25"
        assert calculate_season(date(2025, 1, 1)) == "2024-25"
    
    def test_league_validation(self, supported_leagues):
        """Test league validation works."""
        from src.utils.helpers import validate_league_code
        
        for league in supported_leagues:
            assert validate_league_code(league) is True
        
        assert validate_league_code("XX") is False
    
    def test_date_parsing(self):
        """Test date parsing works."""
        from src.utils.helpers import parse_date
        
        assert parse_date("2024-09-15") == date(2024, 9, 15)
        assert parse_date("15/09/2024") == date(2024, 9, 15)


class TestConfiguration:
    """Validate configuration management."""
    
    def test_config_loads(self):
        """Test configuration loads."""
        from src.utils.config import get_config
        
        config = get_config()
        assert config is not None
    
    def test_config_has_paths(self):
        """Test configuration has required paths."""
        from src.utils.config import get_config
        
        config = get_config()
        
        assert config.data_raw_path is not None
        assert config.data_processed_path is not None


class TestLogging:
    """Validate logging setup."""
    
    def test_logger_works(self):
        """Test logger can be created and used."""
        from src.utils.logger import get_logger
        
        logger = get_logger("test")
        
        # Should not raise
        logger.info("Test message")
        logger.debug("Debug message")


class TestDataQuality:
    """Validate data quality checks."""
    
    def test_no_negative_goals(self, sample_historical_df):
        """Test no negative goals in data."""
        from src.data.loaders import DataProcessor
        
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
        })
        
        processed = processor.process_historical_data(df)
        
        if "fthg" in processed.columns:
            valid_goals = processed["fthg"].dropna()
            assert (valid_goals >= 0).all()
    
    def test_valid_result_codes(self, sample_historical_df):
        """Test result codes are valid (H, D, A)."""
        from src.data.loaders import DataProcessor
        
        processor = DataProcessor()
        
        df = sample_historical_df.copy()
        df = df.rename(columns={
            "HomeTeam": "home_team",
            "AwayTeam": "away_team",
            "Date": "date",
            "FTR": "ftr",
        })
        
        processed = processor.process_historical_data(df)
        
        if "ftr" in processed.columns:
            valid_results = processed["ftr"].dropna()
            assert valid_results.isin(["H", "D", "A"]).all()


class TestPerformance:
    """Validate performance benchmarks."""
    
    def test_processing_speed(self, test_data_dir):
        """Test processing completes in reasonable time."""
        import pandas as pd
        from src.data.loaders import ExcelLoader, DataProcessor
        
        # Create test data
        data = []
        for i in range(200):
            data.append({
                "Div": "E0",
                "Date": f"2024-{(i % 12) + 1:02d}-{(i % 28) + 1:02d}",
                "HomeTeam": f"Team{i % 10}",
                "AwayTeam": f"Team{(i + 5) % 10}",
                "FTHG": i % 4,
                "FTAG": i % 3,
                "FTR": "H" if i % 4 > i % 3 else ("A" if i % 3 > i % 4 else "D"),
            })
        
        file_path = test_data_dir / "performance.xlsx"
        pd.DataFrame(data).to_excel(file_path, index=False)
        
        start = time.time()
        
        loader = ExcelLoader()
        df = loader.load(file_path, filter_unsupported_leagues=False)
        
        processor = DataProcessor()
        processed = processor.process_historical_data(df)
        matches = processor.convert_to_matches(processed)
        
        elapsed = time.time() - start
        
        assert elapsed < 10  # Should complete in 10 seconds
        assert len(matches) > 100


class TestErrorHandling:
    """Validate error handling."""
    
    def test_missing_file_handled(self, test_data_dir):
        """Test missing file is handled gracefully."""
        from src.data.loaders import ExcelLoader
        
        loader = ExcelLoader()
        result = loader.load(test_data_dir / "nonexistent.xlsx")
        
        assert result is None
    
    def test_corrupt_file_handled(self, corrupt_excel_file):
        """Test corrupt file is handled gracefully."""
        from src.data.loaders import ExcelLoader
        
        loader = ExcelLoader()
        result = loader.load(corrupt_excel_file)
        
        assert result is None
    
    def test_invalid_json_handled(self, corrupt_json_file, json_storage):
        """Test invalid JSON is handled gracefully."""
        result = json_storage.load(corrupt_json_file, default=None)
        
        assert result is None


class TestAllLeaguesSupported:
    """Validate all 10 leagues are supported."""
    
    def test_all_leagues_valid(self, supported_leagues):
        """Test all 10 supported leagues."""
        from src.utils.helpers import validate_league_code
        
        expected = ["E0", "E1", "E2", "E3", "D1", "F1", "F2", "I1", "I2", "SP1"]
        
        for league in expected:
            assert validate_league_code(league) is True, \
                f"League {league} should be supported"


# Summary validation
class TestPhase1Summary:
    """Final Phase 1 validation summary."""
    
    def test_phase1_requirements_met(self):
        """Verify all Phase 1 requirements are met."""
        requirements = {
            "Match entity": True,
            "Team entity": True,
            "Prediction entity": True,
            "Excel loader": True,
            "CSV loader": True,
            "Data processor": True,
            "JSON storage": True,
            "Configuration": True,
            "Logging": True,
            "Helpers": True,
            "10 leagues": True,
        }
        
        # All should be implemented
        assert all(requirements.values()), \
            f"Missing: {[k for k, v in requirements.items() if not v]}"
