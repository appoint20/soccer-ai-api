from typing import List
from src.domain.entities.match import Match
from src.domain.value_objects.analysis_date import AnalysisDate
from src.infrastructure.repositories.historical_match_repository import IHistoricalMatchRepository

class TimeTravelHistoricalMatchRepository:
    """
    Repository that provides historical matches with time-travel filtering.
    
    Prevents data leakage by only returning matches BEFORE a cutoff date.
    Critical for accurate backtesting.
    """
    
    def __init__(self, base_repository: IHistoricalMatchRepository):
        self._base_repo = base_repository
    
    def get_matches_before(self, cutoff_date: AnalysisDate) -> List[Match]:
        """
        Get all historical matches BEFORE the cutoff date.
        
        This ensures backtesting uses only data that would have been
        available at the time of prediction.
        """
        all_matches = self._base_repo.get_all()
        
        return [
            match for match in all_matches
            if match.match_date < cutoff_date.value
        ]
    
    def get_match_count_before(self, cutoff_date: AnalysisDate) -> int:
        """Get count of historical matches before cutoff."""
        return len(self.get_matches_before(cutoff_date))
