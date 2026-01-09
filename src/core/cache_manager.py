"""
Cache Manager for API-Football data.

Supports both Redis and in-memory caching with automatic fallback.
"""
import json
import time
from typing import Any, Optional
from datetime import datetime, timedelta
import asyncio

from src.utils.logger import get_logger

logger = get_logger("CacheManager")


class InMemoryCache:
    """Simple in-memory cache with TTL support."""
    
    def __init__(self):
        self._cache: dict = {}
        self._expiry: dict = {}
    
    async def get(self, key: str) -> Optional[str]:
        """Get cached value if not expired."""
        if key in self._cache:
            if time.time() < self._expiry.get(key, 0):
                return self._cache[key]
            else:
                # Expired, remove it
                del self._cache[key]
                del self._expiry[key]
        return None
    
    async def set(self, key: str, value: str, ttl: int = 3600) -> bool:
        """Set value with TTL in seconds."""
        self._cache[key] = value
        self._expiry[key] = time.time() + ttl
        return True
    
    async def incr(self, key: str) -> int:
        """Increment counter."""
        current = int(self._cache.get(key, 0))
        current += 1
        self._cache[key] = str(current)
        if key not in self._expiry:
            # Default 24 hour expiry for counters
            self._expiry[key] = time.time() + 86400
        return current
    
    async def exists(self, key: str) -> bool:
        """Check if key exists and not expired."""
        return await self.get(key) is not None


class CacheManager:
    """
    Cache manager with Redis support and in-memory fallback.
    
    TTL values:
    - Fixtures: 1 hour (3600 seconds)
    - Odds: 30 minutes (1800 seconds)
    - API counter: 24 hours (86400 seconds)
    """
    
    FIXTURES_TTL = 3600  # 1 hour
    ODDS_TTL = 1800  # 30 minutes
    COUNTER_TTL = 86400  # 24 hours
    
    def __init__(self, redis_url: Optional[str] = None):
        self._redis = None
        self._memory_cache = InMemoryCache()
        self._use_redis = False
        
        if redis_url:
            try:
                import redis.asyncio as redis
                self._redis = redis.from_url(redis_url)
                self._use_redis = True
                logger.info("Using Redis cache")
            except ImportError:
                logger.warning("Redis not installed, using in-memory cache")
            except Exception as e:
                logger.warning(f"Redis connection failed: {e}, using in-memory cache")
        else:
            logger.info("Using in-memory cache (no Redis URL provided)")
    
    async def get_fixtures(self, league_id: int, from_date: str, to_date: str) -> Optional[dict]:
        """Get cached fixtures."""
        key = f"fixtures:{league_id}:{from_date}:{to_date}"
        return await self._get_json(key)
    
    async def set_fixtures(self, league_id: int, from_date: str, to_date: str, data: dict) -> bool:
        """Cache fixtures for 1 hour."""
        key = f"fixtures:{league_id}:{from_date}:{to_date}"
        return await self._set_json(key, data, self.FIXTURES_TTL)
    
    async def get_odds(self, fixture_id: int) -> Optional[dict]:
        """Get cached odds."""
        key = f"odds:{fixture_id}"
        return await self._get_json(key)
    
    async def set_odds(self, fixture_id: int, data: dict) -> bool:
        """Cache odds for 30 minutes."""
        key = f"odds:{fixture_id}"
        return await self._set_json(key, data, self.ODDS_TTL)
    
    async def get_api_call_count(self) -> int:
        """Get today's API call count."""
        key = self._get_counter_key()
        value = await self._get(key)
        return int(value) if value else 0
    
    async def increment_api_call_count(self) -> int:
        """Increment API call counter."""
        key = self._get_counter_key()
        if self._use_redis and self._redis:
            try:
                count = await self._redis.incr(key)
                await self._redis.expire(key, self.COUNTER_TTL)
                return count
            except Exception as e:
                logger.error(f"Redis incr failed: {e}")
        return await self._memory_cache.incr(key)
    
    def _get_counter_key(self) -> str:
        """Get counter key for today."""
        today = datetime.now().strftime("%Y-%m-%d")
        return f"api_calls:{today}"
    
    async def _get(self, key: str) -> Optional[str]:
        """Get raw value from cache."""
        if self._use_redis and self._redis:
            try:
                value = await self._redis.get(key)
                if value:
                    logger.debug(f"Cache HIT (Redis): {key}")
                    return value.decode() if isinstance(value, bytes) else value
            except Exception as e:
                logger.error(f"Redis get failed: {e}")
        
        value = await self._memory_cache.get(key)
        if value:
            logger.debug(f"Cache HIT (memory): {key}")
        else:
            logger.debug(f"Cache MISS: {key}")
        return value
    
    async def _set(self, key: str, value: str, ttl: int) -> bool:
        """Set raw value in cache."""
        if self._use_redis and self._redis:
            try:
                await self._redis.setex(key, ttl, value)
                return True
            except Exception as e:
                logger.error(f"Redis set failed: {e}")
        return await self._memory_cache.set(key, value, ttl)
    
    async def _get_json(self, key: str) -> Optional[dict]:
        """Get JSON value from cache."""
        value = await self._get(key)
        if value:
            try:
                return json.loads(value)
            except json.JSONDecodeError:
                pass
        return None
    
    async def _set_json(self, key: str, data: dict, ttl: int) -> bool:
        """Set JSON value in cache."""
        return await self._set(key, json.dumps(data), ttl)


# Global singleton
_cache_manager: Optional[CacheManager] = None


def get_cache_manager(redis_url: Optional[str] = None) -> CacheManager:
    """Get or create cache manager singleton."""
    global _cache_manager
    if _cache_manager is None:
        _cache_manager = CacheManager(redis_url)
    return _cache_manager
