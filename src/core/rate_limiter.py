"""
API Rate Limiter.

Enforces 100 API calls per day limit for API-Football.
"""
from typing import Tuple

from src.core.cache_manager import get_cache_manager, CacheManager
from src.utils.logger import get_logger

logger = get_logger("RateLimiter")


class RateLimitExceeded(Exception):
    """Raised when daily API limit is exceeded."""
    
    def __init__(self, current: int, max_calls: int, remaining: int):
        self.current = current
        self.max_calls = max_calls
        self.remaining = remaining
        super().__init__(f"API rate limit exceeded: {current}/{max_calls} calls used")


class RateLimiter:
    """
    Rate limiter for API-Football.
    
    Enforces maximum 100 API calls per day.
    Tracks calls in cache (Redis or memory).
    """
    
    DEFAULT_MAX_CALLS = 100
    
    def __init__(self, cache: CacheManager, max_daily_calls: int = DEFAULT_MAX_CALLS):
        self._cache = cache
        self._max_calls = max_daily_calls
    
    async def check_limit(self) -> Tuple[bool, int, int]:
        """
        Check if API call is allowed.
        
        Returns:
            Tuple of (allowed, current_count, remaining_calls)
        """
        current = await self._cache.get_api_call_count()
        remaining = max(0, self._max_calls - current)
        allowed = current < self._max_calls
        
        if not allowed:
            logger.warning(f"Rate limit reached: {current}/{self._max_calls}")
        
        return allowed, current, remaining
    
    async def record_call(self) -> Tuple[int, int]:
        """
        Record an API call.
        
        Returns:
            Tuple of (new_count, remaining_calls)
        
        Raises:
            RateLimitExceeded: If limit is exceeded
        """
        allowed, current, remaining = await self.check_limit()
        
        if not allowed:
            raise RateLimitExceeded(current, self._max_calls, remaining)
        
        new_count = await self._cache.increment_api_call_count()
        new_remaining = max(0, self._max_calls - new_count)
        
        logger.info(f"API call recorded: {new_count}/{self._max_calls} (remaining: {new_remaining})")
        
        return new_count, new_remaining
    
    async def get_usage_stats(self) -> dict:
        """
        Get current API usage statistics.
        
        Returns:
            Dict with usage stats
        """
        current = await self._cache.get_api_call_count()
        remaining = max(0, self._max_calls - current)
        usage_pct = round((current / self._max_calls) * 100, 1)
        
        return {
            "total_calls_today": current,
            "remaining_calls": remaining,
            "max_daily_calls": self._max_calls,
            "usage_percentage": usage_pct,
        }


# Global singleton
_rate_limiter = None


def get_rate_limiter(max_daily_calls: int = 100) -> RateLimiter:
    """Get or create rate limiter singleton."""
    global _rate_limiter
    if _rate_limiter is None:
        cache = get_cache_manager()
        _rate_limiter = RateLimiter(cache, max_daily_calls)
    return _rate_limiter
