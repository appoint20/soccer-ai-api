"""
Derby detection service.

Identifies derby matches and provides intensity information
based on historical rivalry data.
"""
import json
from pathlib import Path
from typing import Dict, Optional, Set, Tuple

from src.domain.services.base_service import BaseService


class DerbyService(BaseService):
    """
    Service for detecting derby matches between rival teams.
    
    Uses derby_rivalries.json data to identify matches that are
    local derbies or historic rivalries.
    """
    
    # Intensity levels mapped to numeric values
    INTENSITY_MAP = {
        "low": 1,
        "medium": 2,
        "high": 3,
        "very-high": 4,
    }
    
    def __init__(self, rivalries_path: str = "data/derby_rivalries.json"):
        """
        Initialize derby service.
        
        Args:
            rivalries_path: Path to derby rivalries JSON file
        """
        super().__init__()
        
        self.rivalries_path = Path(rivalries_path)
        self._rivalry_pairs: Dict[Tuple[str, str], Dict] = {}
        self._team_aliases: Dict[str, str] = {}
        
        self._load_rivalries()
    
    def _load_rivalries(self) -> None:
        """Load rivalry data from JSON file."""
        if not self.rivalries_path.exists():
            self.logger.warning(f"Rivalries file not found: {self.rivalries_path}")
            return
        
        try:
            with open(self.rivalries_path, 'r', encoding='utf-8') as f:
                data = json.load(f)
            
            # Build lookup dictionary for fast O(1) access
            leagues = data.get("leagues", {})
            
            for league_key, league_data in leagues.items():
                rivalries = league_data.get("rivalries", [])
                
                for rivalry in rivalries:
                    teams = rivalry.get("teams", [])
                    if len(teams) >= 2:
                        team1 = self._normalize_team_name(teams[0])
                        team2 = self._normalize_team_name(teams[1])
                        
                        # Store both orderings
                        key1 = (team1, team2)
                        key2 = (team2, team1)
                        
                        rivalry_info = {
                            "name": rivalry.get("name", ""),
                            "intensity": rivalry.get("intensity", "medium"),
                            "description": rivalry.get("description", ""),
                            "league": league_data.get("name", ""),
                        }
                        
                        self._rivalry_pairs[key1] = rivalry_info
                        self._rivalry_pairs[key2] = rivalry_info
            
            self.logger.info(f"Loaded {len(self._rivalry_pairs) // 2} rivalries")
            
        except Exception as e:
            self.logger.error(f"Failed to load rivalries: {e}")
    
    def _normalize_team_name(self, name: str) -> str:
        """
        Normalize team name for matching.
        
        Args:
            name: Original team name
            
        Returns:
            Normalized lowercase name
        """
        if not name:
            return ""
        
        # Convert to lowercase and strip whitespace
        normalized = name.lower().strip()
        
        # Common substitutions for matching
        substitutions = {
            "manchester united": "man united",
            "manchester city": "man city",
            "borussia dortmund": "dortmund",
            "borussia mönchengladbach": "monchengladbach",
            "bayern münchen": "bayern munich",
            "atlético madrid": "atletico madrid",
            "fc köln": "koln",
            "psg": "paris saint-germain",
            "paris sg": "paris saint-germain",
        }
        
        return substitutions.get(normalized, normalized)
    
    def is_derby(self, home_team: str, away_team: str) -> bool:
        """
        Check if a match is a derby.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            
        Returns:
            True if the match is a derby
        """
        home_norm = self._normalize_team_name(home_team)
        away_norm = self._normalize_team_name(away_team)
        
        return (home_norm, away_norm) in self._rivalry_pairs
    
    def get_derby_info(
        self,
        home_team: str,
        away_team: str,
    ) -> Optional[Dict]:
        """
        Get derby information for a match.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            
        Returns:
            Dict with derby info or None if not a derby
        """
        home_norm = self._normalize_team_name(home_team)
        away_norm = self._normalize_team_name(away_team)
        
        return self._rivalry_pairs.get((home_norm, away_norm))
    
    def get_intensity(self, home_team: str, away_team: str) -> int:
        """
        Get derby intensity as numeric value.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            
        Returns:
            0 = not a derby, 1-4 = intensity level
        """
        derby_info = self.get_derby_info(home_team, away_team)
        
        if not derby_info:
            return 0
        
        intensity_str = derby_info.get("intensity", "medium")
        return self.INTENSITY_MAP.get(intensity_str, 2)
    
    def get_derby_features(
        self,
        home_team: str,
        away_team: str,
    ) -> Dict[str, float]:
        """
        Get derby features for ML model.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            
        Returns:
            Dict with derby features
        """
        is_derby = self.is_derby(home_team, away_team)
        intensity = self.get_intensity(home_team, away_team)
        
        return {
            "is_derby": 1.0 if is_derby else 0.0,
            "derby_intensity": float(intensity),
        }
