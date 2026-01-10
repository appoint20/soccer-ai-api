from typing import Dict, Optional
import json
from src.utils.logger import get_logger

logger = get_logger("TeamNameMatcher")

class TeamNameMatcher:
    """
    Domain service for matching team names with fuzzy logic.
    
    Responsibilities:
    - Normalize team names
    - Match variations (e.g., "Man United" vs "Manchester United")
    - Load and apply team name mappings
    """
    
    def __init__(self, mapping_path: str = "data/team_name_mapping.json"):
        self._mapping_path = mapping_path
        self._mapping: Optional[Dict[str, str]] = None
    
    def matches(self, name1: str, name2: str) -> bool:
        """Check if two team names refer to the same team."""
        normalized1 = self.normalize(name1)
        normalized2 = self.normalize(name2)
        
        # Exact match
        if normalized1 == normalized2:
            return True
        
        # Substring match
        if normalized1 in normalized2 or normalized2 in normalized1:
            return True
        
        # First word match
        if self._first_word_matches(normalized1, normalized2):
            return True
        
        return False
    
    def normalize(self, name: str) -> str:
        """Normalize team name using mapping and common rules."""
        name_lower = name.lower().strip()
        
        # Try direct mapping lookup
        mapping = self._get_mapping()
        if name_lower in mapping:
            return mapping[name_lower]
        
        # Apply common transformations
        normalized = self._apply_transformations(name_lower)
        
        # Try mapping again after transformation
        if normalized in mapping:
            return mapping[normalized]
        
        return normalized
    
    def _get_mapping(self) -> Dict[str, str]:
        """Load team name mapping (cached)."""
        if self._mapping is None:
            self._mapping = self._load_mapping()
        return self._mapping
    
    def _load_mapping(self) -> Dict[str, str]:
        """Load mapping from JSON file."""
        try:
            with open(self._mapping_path, 'r') as f:
                data = json.load(f)
            
            # Flatten nested structure
            flat = {}
            for league, teams in data.items():
                if not league.startswith("_"):
                    for sofascore, fd_name in teams.items():
                        flat[sofascore.lower()] = fd_name.lower()
                        flat[fd_name.lower()] = fd_name.lower()
            
            return flat
        except Exception as e:
            logger.warning(f"Could not load team mapping: {e}")
            return {}
    
    def _apply_transformations(self, name: str) -> str:
        """Apply common name transformations."""
        # Remove suffixes
        for suffix in [" fc", " afc", " sc", " united", " city"]:
            name = name.replace(suffix, "")
        return name.strip()
    
    def _first_word_matches(self, name1: str, name2: str) -> bool:
        """Check if first words match."""
        words1 = name1.split()
        words2 = name2.split()
        return bool(words1 and words2 and words1[0] == words2[0])
