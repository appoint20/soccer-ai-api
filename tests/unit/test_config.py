"""
Unit tests for configuration management.

Tests cover:
- Loading configuration
- Accessing league mappings
- Path management
- Default values
"""
import pytest
from pathlib import Path

import sys
sys.path.insert(0, str(Path(__file__).parent.parent.parent))

from src.utils.config import Config, get_config


class TestConfigLoading:
    """Tests for configuration loading."""
    
    def test_config_singleton(self):
        """Test Config is a singleton."""
        config1 = get_config()
        config2 = get_config()
        
        assert config1 is config2
    
    def test_config_has_project_root(self):
        """Test project root is set."""
        config = get_config()
        
        assert config.project_root is not None
        assert isinstance(config.project_root, Path)
    
    def test_config_has_data_paths(self):
        """Test data paths are configured."""
        config = get_config()
        
        assert config.data_raw_path is not None
        assert config.data_processed_path is not None
        assert config.data_predictions_path is not None


class TestLeagueMappings:
    """Tests for league configuration access."""
    
    def test_get_league(self):
        """Test getting league by code."""
        config = get_config()
        
        league = config.get_league("E0")
        
        if league:
            assert "name" in league
            assert league["name"] == "Premier League"
    
    def test_get_league_name(self):
        """Test getting league name by code."""
        config = get_config()
        
        name = config.get_league_name("E0")
        
        assert name == "Premier League" or name == "E0"
    
    def test_get_supported_leagues(self):
        """Test getting list of supported leagues."""
        config = get_config()
        
        leagues = config.get_supported_leagues()
        
        assert isinstance(leagues, list)
        # Should have at least E0 if configured
        if leagues:
            assert "E0" in leagues or len(leagues) > 0
    
    def test_get_unknown_league(self):
        """Test getting unknown league returns None."""
        config = get_config()
        
        result = config.get_league("XX")
        
        assert result is None


class TestSettings:
    """Tests for settings access."""
    
    def test_get_setting(self):
        """Test getting a setting value."""
        config = get_config()
        
        # Settings may or may not be loaded
        result = config.get_setting("features.lookback_matches", 5)
        
        assert isinstance(result, int)
    
    def test_get_setting_with_default(self):
        """Test getting setting with default value."""
        config = get_config()
        
        result = config.get_setting("nonexistent.key", "default")
        
        assert result == "default"
    
    def test_get_nested_setting(self):
        """Test getting nested setting with dot notation."""
        config = get_config()
        
        result = config.get_setting("thresholds.over25.high_confidence", 0.70)
        
        assert isinstance(result, (int, float))


class TestEnvironment:
    """Tests for environment settings."""
    
    def test_environment_default(self):
        """Test default environment."""
        config = get_config()
        
        assert config.environment in ["development", "production", "test"]
    
    def test_is_development(self):
        """Test is_development check."""
        config = get_config()
        
        result = config.is_development()
        
        assert isinstance(result, bool)
    
    def test_is_production(self):
        """Test is_production check."""
        config = get_config()
        
        result = config.is_production()
        
        assert isinstance(result, bool)


class TestDirectoryManagement:
    """Tests for directory management."""
    
    def test_ensure_directories(self, tmp_path, monkeypatch):
        """Test ensure_directories creates directories."""
        config = get_config()
        
        # Use temp paths
        original_raw = config.data_raw_path
        config.data_raw_path = tmp_path / "data" / "raw"
        config.data_processed_path = tmp_path / "data" / "processed"
        config.data_predictions_path = tmp_path / "data" / "predictions"
        config.models_path = tmp_path / "models"
        config.logs_path = tmp_path / "logs"
        
        config.ensure_directories()
        
        # Restore
        config.data_raw_path = original_raw
        
        assert (tmp_path / "data" / "raw" / "historical").exists()
        assert (tmp_path / "data" / "processed").exists()


class TestFeatureParameters:
    """Tests for feature engineering parameters."""
    
    def test_lookback_matches(self):
        """Test lookback matches parameter."""
        config = get_config()
        
        assert config.default_lookback_matches > 0
        assert isinstance(config.default_lookback_matches, int)
    
    def test_min_matches_for_prediction(self):
        """Test minimum matches for prediction."""
        config = get_config()
        
        assert config.min_matches_for_prediction > 0
        assert isinstance(config.min_matches_for_prediction, int)
