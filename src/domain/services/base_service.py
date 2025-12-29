"""
Base service class for all feature engineering services.

Provides common functionality: logging, caching, validation,
and performance tracking.
"""
from abc import ABC, abstractmethod
from datetime import date, datetime
from typing import Any, Optional, Dict, List
import time

from src.utils.logger import get_logger
from src.data.cache.cache_manager import CacheManager, get_cache


class BaseService(ABC):
    """
    Abstract base class for all feature engineering services.
    
    Provides:
    - Consistent logging
    - Cache integration
    - Input validation
    - Performance tracking
    """
    
    def __init__(
        self,
        cache_manager: Optional[CacheManager] = None,
        cache_ttl: int = 3600,
    ):
        """
        Initialize base service.
        
        Args:
            cache_manager: Optional cache manager instance
            cache_ttl: Default cache TTL in seconds
        """
        self.cache = cache_manager or get_cache()
        self.cache_ttl = cache_ttl
        self.logger = get_logger(self.__class__.__name__)
        
        self._stats = {
            "calls": 0,
            "cache_hits": 0,
            "cache_misses": 0,
            "total_time_ms": 0,
        }
    
    def get_cached(self, cache_key: str) -> Optional[Any]:
        """
        Get result from cache.
        
        Args:
            cache_key: Cache key to look up
            
        Returns:
            Cached value or None
        """
        result = self.cache.get(cache_key)
        if result is not None:
            self._stats["cache_hits"] += 1
        else:
            self._stats["cache_misses"] += 1
        return result
    
    def set_cached(
        self, 
        cache_key: str, 
        data: Any,
        ttl: Optional[int] = None
    ) -> None:
        """
        Save result to cache.
        
        Args:
            cache_key: Cache key
            data: Data to cache
            ttl: Override TTL (uses default if not specified)
        """
        self.cache.set(cache_key, data, ttl or self.cache_ttl)
    
    def invalidate_cache(self, pattern: Optional[str] = None) -> int:
        """
        Invalidate cache entries.
        
        Args:
            pattern: Optional pattern to match (invalidates all if None)
            
        Returns:
            Number of entries invalidated
        """
        if pattern:
            return self.cache.invalidate(pattern)
        else:
            return self.cache.clear_all()
    
    def generate_cache_key(self, *parts) -> str:
        """
        Generate cache key from parts.
        
        Args:
            *parts: Key components
            
        Returns:
            Cache key string
        """
        return self.cache.generate_key(self.__class__.__name__, *parts)
    
    def track_performance(self, operation: str):
        """
        Context manager for tracking operation performance.
        
        Usage:
            with self.track_performance("calculate"):
                # ... operation ...
        """
        return PerformanceTracker(self, operation)
    
    def validate_date(self, dt: Any) -> Optional[date]:
        """
        Validate and parse date input.
        
        Args:
            dt: Date input (date, datetime, or string)
            
        Returns:
            date object or None if invalid
        """
        if dt is None:
            return None
        
        if isinstance(dt, date) and not isinstance(dt, datetime):
            return dt
        
        if isinstance(dt, datetime):
            return dt.date()
        
        if isinstance(dt, str):
            try:
                return datetime.fromisoformat(dt[:10]).date()
            except (ValueError, TypeError):
                return None
        
        return None
    
    def validate_team_name(self, team_name: Any) -> Optional[str]:
        """
        Validate team name input.
        
        Args:
            team_name: Team name input
            
        Returns:
            Cleaned team name or None if invalid
        """
        if team_name is None:
            return None
        
        name = str(team_name).strip()
        if not name or name.lower() in ["nan", "none", ""]:
            return None
        
        return name
    
    @staticmethod
    def filter_matches_for_team(
            matches: List,
        team_name: str,
        as_of_date: Optional[date] = None,
        home_only: bool = False,
        away_only: bool = False,
    ) -> List:
        """
        Filter matches for a specific team.
        
        Args:
            matches: List of Match objects or dicts
            team_name: Team to filter for
            as_of_date: Only include matches before this date
            home_only: Only return home matches
            away_only: Only return away matches
            
        Returns:
            Filtered list of matches
        """
        result = []
        
        for match in matches:
            # Get fields based on type
            if isinstance(match, dict):
                home = match.get("home_team", "")
                away = match.get("away_team", "")
                match_date = match.get("match_date")
            else:
                home = getattr(match, "home_team", "")
                away = getattr(match, "away_team", "")
                match_date = getattr(match, "match_date", None)
            
            # Check team involvement
            is_home = team_name == home
            is_away = team_name == away
            
            if not is_home and not is_away:
                continue
            
            if home_only and not is_home:
                continue
            
            if away_only and not is_away:
                continue
            
            # Check date (time-travel)
            if as_of_date:
                if isinstance(match_date, str):
                    try:
                        match_date = datetime.fromisoformat(match_date[:10]).date()
                    except (ValueError, TypeError):
                        continue
                
                if match_date and match_date >= as_of_date:
                    continue
            
            result.append(match)
        
        return result
    
    def log_operation(self, operation: str, details: str = "") -> None:
        """
        Log an operation.
        
        Args:
            operation: Operation name
            details: Additional details
        """
        self.logger.debug(f"{operation}: {details}" if details else operation)
    
    def get_stats(self) -> Dict[str, Any]:
        """
        Get service statistics.
        
        Returns:
            Dict with service stats
        """
        return {
            **self._stats,
            "cache_hit_rate": (
                self._stats["cache_hits"] / 
                max(1, self._stats["cache_hits"] + self._stats["cache_misses"])
            ),
        }


class PerformanceTracker:
    """Context manager for tracking operation performance."""
    
    def __init__(self, service: BaseService, operation: str):
        self.service = service
        self.operation = operation
        self.start_time = None
    
    def __enter__(self):
        self.start_time = time.time()
        self.service._stats["calls"] += 1
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        elapsed_ms = (time.time() - self.start_time) * 1000
        self.service._stats["total_time_ms"] += elapsed_ms
        
        if elapsed_ms > 100:  # Log slow operations
            self.service.logger.debug(
                f"{self.operation} took {elapsed_ms:.1f}ms"
            )
