"""
Cache manager for feature engineering services.

Provides in-memory caching with TTL and LRU eviction to avoid
recalculating expensive statistics.
"""
import time
from collections import OrderedDict
from datetime import datetime
from typing import Any, Optional, Dict
from threading import Lock

from src.utils.logger import get_logger


class CacheManager:
    """
    In-memory cache manager with TTL and LRU eviction.
    
    Features:
    - Time-to-live (TTL) expiration
    - LRU (Least Recently Used) eviction when max size reached
    - Thread-safe operations
    - Cache statistics tracking
    """
    
    def __init__(
        self,
        max_size: int = 10000,
        default_ttl: int = 3600,
    ):
        """
        Initialize cache manager.
        
        Args:
            max_size: Maximum number of items to store
            default_ttl: Default time-to-live in seconds
        """
        self.max_size = max_size
        self.default_ttl = default_ttl
        
        self._cache: OrderedDict[str, Dict[str, Any]] = OrderedDict()
        self._lock = Lock()
        self._hits = 0
        self._misses = 0
        
        self.logger = get_logger("CacheManager")
    
    def get(self, key: str) -> Optional[Any]:
        """
        Get cached value if exists and not expired.
        
        Args:
            key: Cache key
            
        Returns:
            Cached value or None if not found/expired
        """
        with self._lock:
            if key not in self._cache:
                self._misses += 1
                return None
            
            entry = self._cache[key]
            
            # Check expiration
            if entry["expires_at"] < time.time():
                del self._cache[key]
                self._misses += 1
                return None
            
            # Move to end (most recently used)
            self._cache.move_to_end(key)
            self._hits += 1
            
            return entry["value"]
    
    def set(
        self, 
        key: str, 
        value: Any, 
        ttl: Optional[int] = None
    ) -> None:
        """
        Cache value with expiration.
        
        Args:
            key: Cache key
            value: Value to cache
            ttl: Time-to-live in seconds (uses default if not specified)
        """
        if ttl is None:
            ttl = self.default_ttl
        
        with self._lock:
            # Remove oldest items if at max size
            while len(self._cache) >= self.max_size:
                self._cache.popitem(last=False)
            
            self._cache[key] = {
                "value": value,
                "expires_at": time.time() + ttl,
                "created_at": time.time(),
            }
            
            # Move to end
            self._cache.move_to_end(key)
    
    def invalidate(self, pattern: str) -> int:
        """
        Invalidate cache keys matching pattern.
        
        Args:
            pattern: String pattern to match (uses 'in' matching)
            
        Returns:
            Number of keys invalidated
        """
        with self._lock:
            keys_to_remove = [
                k for k in self._cache.keys() 
                if pattern in k
            ]
            
            for key in keys_to_remove:
                del self._cache[key]
            
            if keys_to_remove:
                self.logger.debug(
                    f"Invalidated {len(keys_to_remove)} cache entries "
                    f"matching '{pattern}'"
                )
            
            return len(keys_to_remove)
    
    def invalidate_team(self, team_name: str) -> int:
        """
        Invalidate all cache entries for a specific team.
        
        Args:
            team_name: Team name
            
        Returns:
            Number of entries invalidated
        """
        return self.invalidate(team_name)
    
    def clear_all(self) -> int:
        """
        Clear entire cache.
        
        Returns:
            Number of entries cleared
        """
        with self._lock:
            count = len(self._cache)
            self._cache.clear()
            self._hits = 0
            self._misses = 0
            
            self.logger.info(f"Cleared {count} cache entries")
            return count
    
    def exists(self, key: str) -> bool:
        """
        Check if key exists and is not expired.
        
        Args:
            key: Cache key
            
        Returns:
            True if key exists and is valid
        """
        with self._lock:
            if key not in self._cache:
                return False
            
            return self._cache[key]["expires_at"] >= time.time()
    
    def get_stats(self) -> Dict[str, Any]:
        """
        Get cache statistics.
        
        Returns:
            Dict with cache stats
        """
        with self._lock:
            total_requests = self._hits + self._misses
            hit_rate = self._hits / total_requests if total_requests > 0 else 0.0
            
            return {
                "size": len(self._cache),
                "max_size": self.max_size,
                "hits": self._hits,
                "misses": self._misses,
                "hit_rate": round(hit_rate, 3),
                "default_ttl": self.default_ttl,
            }
    
    def cleanup_expired(self) -> int:
        """
        Remove all expired entries.
        
        Returns:
            Number of entries removed
        """
        with self._lock:
            current_time = time.time()
            expired_keys = [
                k for k, v in self._cache.items()
                if v["expires_at"] < current_time
            ]
            
            for key in expired_keys:
                del self._cache[key]
            
            if expired_keys:
                self.logger.debug(f"Cleaned up {len(expired_keys)} expired entries")
            
            return len(expired_keys)
    
    def get_or_compute(
        self,
        key: str,
        compute_fn: callable,
        ttl: Optional[int] = None
    ) -> Any:
        """
        Get cached value or compute and cache if not found.
        
        Args:
            key: Cache key
            compute_fn: Function to compute value if not cached
            ttl: Time-to-live for new cache entry
            
        Returns:
            Cached or computed value
        """
        value = self.get(key)
        
        if value is not None:
            return value
        
        # Compute value
        value = compute_fn()
        
        # Cache result
        self.set(key, value, ttl)
        
        return value
    
    def generate_key(self, *parts: str) -> str:
        """
        Generate cache key from parts.
        
        Args:
            *parts: Key components
            
        Returns:
            Cache key string
        """
        return ":".join(str(p) for p in parts)


# Singleton instance
_cache_instance: Optional[CacheManager] = None


def get_cache() -> CacheManager:
    """Get the singleton cache instance."""
    global _cache_instance
    if _cache_instance is None:
        _cache_instance = CacheManager()
    return _cache_instance
