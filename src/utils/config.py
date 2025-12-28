"""Configuration management using environment variables and YAML."""
import os
from pathlib import Path
from typing import Any, Optional
from functools import lru_cache

import yaml
from dotenv import load_dotenv


class Config:
    """
    Configuration manager that loads settings from environment variables,
    .env files, and YAML configuration files.
    """
    
    _instance: Optional["Config"] = None
    
    def __new__(cls):
        """Singleton pattern to ensure one config instance."""
        if cls._instance is None:
            cls._instance = super().__new__(cls)
            cls._instance._initialized = False
        return cls._instance
    
    def __init__(self):
        """Initialize configuration."""
        if self._initialized:
            return
        
        # Load .env file
        load_dotenv()
        
        # Get project root (parent of src)
        self.project_root = Path(__file__).parent.parent.parent.parent
        
        # Load paths from environment
        self.data_raw_path = self._get_path("DATA_RAW_PATH", "data/raw")
        self.data_processed_path = self._get_path("DATA_PROCESSED_PATH", "data/processed")
        self.data_predictions_path = self._get_path("DATA_PREDICTIONS_PATH", "data/predictions")
        self.models_path = self._get_path("MODELS_PATH", "models")
        self.config_path = self._get_path("CONFIG_PATH", "config")
        self.logs_path = self._get_path("LOGS_PATH", "logs")
        
        # Environment settings
        self.environment = os.getenv("ENVIRONMENT", "development")
        self.log_level = os.getenv("LOG_LEVEL", "DEBUG")
        self.log_file = os.getenv("LOG_FILE", "logs/app.log")
        
        # Feature engineering parameters
        self.default_lookback_matches = int(
            os.getenv("DEFAULT_LOOKBACK_MATCHES", "5")
        )
        self.min_matches_for_prediction = int(
            os.getenv("MIN_MATCHES_FOR_PREDICTION", "3")
        )
        
        # Load league configurations
        self._leagues: dict = {}
        self._settings: dict = {}
        self._load_config_files()
        
        self._initialized = True
    
    def _get_path(self, env_var: str, default: str) -> Path:
        """Get a path from environment or use default."""
        path_str = os.getenv(env_var, default)
        path = Path(path_str)
        
        # If relative, make it relative to project root
        if not path.is_absolute():
            path = self.project_root / path
        
        return path
    
    def _load_config_files(self) -> None:
        """Load configuration files from config directory."""
        # Load leagues.json
        leagues_file = self.config_path / "leagues.json"
        if leagues_file.exists():
            import json
            with open(leagues_file, "r") as f:
                self._leagues = json.load(f)
        
        # Load settings.yaml
        settings_file = self.config_path / "settings.yaml"
        if settings_file.exists():
            with open(settings_file, "r") as f:
                self._settings = yaml.safe_load(f) or {}
    
    @property
    def leagues(self) -> dict:
        """Get league configurations."""
        return self._leagues
    
    @property
    def settings(self) -> dict:
        """Get application settings."""
        return self._settings
    
    def get_league(self, code: str) -> Optional[dict]:
        """
        Get league info by code.
        
        Args:
            code: League code (e.g., 'E0')
            
        Returns:
            League configuration dict or None if not found
        """
        return self._leagues.get(code)
    
    def get_league_name(self, code: str) -> str:
        """
        Get league full name by code.
        
        Args:
            code: League code
            
        Returns:
            League name or code if not found
        """
        league = self.get_league(code)
        if league:
            return league.get("name", code)
        return code
    
    def get_supported_leagues(self) -> list[str]:
        """Get list of supported league codes."""
        return list(self._leagues.keys())
    
    def get_setting(self, key: str, default: Any = None) -> Any:
        """
        Get a setting by key with dot notation support.
        
        Args:
            key: Setting key (e.g., 'model.threshold')
            default: Default value if not found
            
        Returns:
            Setting value or default
        """
        keys = key.split(".")
        value = self._settings
        
        for k in keys:
            if isinstance(value, dict) and k in value:
                value = value[k]
            else:
                return default
        
        return value
    
    def ensure_directories(self) -> None:
        """Create all required directories if they don't exist."""
        directories = [
            self.data_raw_path / "historical",
            self.data_raw_path / "upcoming",
            self.data_processed_path,
            self.data_predictions_path,
            self.models_path / "tier1",
            self.models_path / "tier2",
            self.models_path / "tier3",
            self.logs_path,
        ]
        
        for directory in directories:
            directory.mkdir(parents=True, exist_ok=True)
    
    def is_development(self) -> bool:
        """Check if running in development mode."""
        return self.environment.lower() == "development"
    
    def is_production(self) -> bool:
        """Check if running in production mode."""
        return self.environment.lower() == "production"


@lru_cache(maxsize=1)
def get_config() -> Config:
    """
    Get the configuration singleton.
    
    Returns:
        Config instance
    """
    return Config()
