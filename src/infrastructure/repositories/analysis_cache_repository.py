from typing import Optional, List, Protocol
from src.domain.entities.analysis_result import MatchAnalysisResult
from src.domain.value_objects.analysis_date import AnalysisDate
from src.infrastructure.cache.firestore_cache import get_cache, FirestoreCache
from src.utils.logger import get_logger

logger = get_logger("AnalysisCacheRepo")

class IAnalysisCacheRepository(Protocol):
    """Interface for caching match analyses."""
    
    def save(self, match_id: str, analysis: MatchAnalysisResult) -> None:
        """Save analysis to cache."""
        ...
    
    def get(self, match_id: str) -> Optional[MatchAnalysisResult]:
        """Get cached analysis."""
        ...
    
    def exists(self, match_id: str) -> bool:
        """Check if analysis exists in cache."""
        ...
    
    def get_for_date(self, date: AnalysisDate) -> List[MatchAnalysisResult]:
        """Get all cached analyses for a date."""
        ...


class FirestoreAnalysisCacheRepository:
    """
    Repository implementation using FirestoreCache backend.
    Adapts the storage-agnostic FirestoreCache to the Repository pattern.
    """
    
    def __init__(self, cache_backend: Optional[FirestoreCache] = None):
        # Use provided backend or singleton
        self._cache = cache_backend or get_cache()
    
    def save(self, match_id: str, analysis: MatchAnalysisResult) -> None:
        try:
            self._cache.save_match_analysis(match_id, analysis.to_dict())
        except Exception as e:
            logger.error(f"Failed to save analysis for {match_id}: {e}")
            raise

    def get(self, match_id: str) -> Optional[MatchAnalysisResult]:
        try:
            data = self._cache.get_match_analysis(match_id)
            if data:
                return MatchAnalysisResult.from_dict(data)
        except Exception as e:
            logger.error(f"Failed to get analysis for {match_id}: {e}")
        return None
    
    def exists(self, match_id: str) -> bool:
        # Currently implemented via get(), optimization possible if backend supports it
        return self.get(match_id) is not None

    def get_for_date(self, date: AnalysisDate) -> List[MatchAnalysisResult]:
        try:
            full_dict = self._cache.get_all_match_analyses(date=date.to_string())
            if not full_dict:
                return []
            return [MatchAnalysisResult.from_dict(d) for d in full_dict.values()]
        except Exception as e:
            logger.error(f"Failed to get analyses for date {date}: {e}")
            return []
