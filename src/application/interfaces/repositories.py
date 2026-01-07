"""
Repository interfaces (Ports).

These are abstract interfaces that define how the application layer
interacts with data sources. Implementations live in infrastructure layer.
"""
from abc import ABC, abstractmethod
from datetime import date
from typing import List, Optional, Protocol

from src.domain.entities.match import Match


class IUpcomingMatchRepository(Protocol):
    """
    Repository for upcoming matches (fixtures).
    
    Responsibility: Retrieve upcoming match fixtures from data source.
    """
    
    def get_by_date(self, target_date: Optional[str] = None) -> List[Match]:
        """
        Get upcoming matches for a specific date.
        
        Args:
            target_date: Date string (YYYY-MM-DD) or None for today
            
        Returns:
            List of upcoming Match entities
        """
        ...
    
    def get_all(self) -> List[Match]:
        """Get all upcoming matches."""
        ...


class IHistoricalMatchRepository(Protocol):
    """
    Repository for historical match data.
    
    Responsibility: Retrieve completed matches for analysis.
    """
    
    def get_all(self) -> List[Match]:
        """Get all historical matches."""
        ...
    
    def get_by_team(self, team_name: str, last_n: int = 5) -> List[Match]:
        """
        Get recent matches for a specific team.
        
        Args:
            team_name: Team name to search for
            last_n: Number of recent matches to return
            
        Returns:
            List of Match entities, sorted by date descending
        """
        ...
    
    def get_h2h(
        self, 
        team_a: str, 
        team_b: str, 
        last_n: int = 5
    ) -> List[Match]:
        """
        Get head-to-head matches between two teams.
        
        Args:
            team_a: First team name
            team_b: Second team name
            last_n: Number of recent H2H matches to return
            
        Returns:
            List of Match entities where both teams played each other
        """
        ...
