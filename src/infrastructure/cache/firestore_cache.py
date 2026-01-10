"""
Firestore Cache Module.

Provides persistent caching for AI analysis using Google Cloud Firestore.
Falls back to local file cache if Firestore is unavailable.
"""
import os
import json
from datetime import datetime
from typing import Dict, Any, Optional
from dataclasses import asdict

from src.utils.logger import get_logger

logger = get_logger("FirestoreCache")

# Firestore client (lazy loaded)
_db = None


def get_firestore_client():
    """Get Firestore client, initializing if needed."""
    global _db
    
    if _db is not None:
        return _db
    
    try:
        from google.cloud import firestore
        _db = firestore.Client()
        logger.info("Firestore client initialized successfully")
        return _db
    except ImportError:
        logger.warning("google-cloud-firestore not installed, using file cache fallback")
        return None
    except Exception as e:
        logger.warning(f"Firestore initialization failed: {e}, using file cache fallback")
        return None


class FirestoreCache:
    """
    Cache manager with Firestore backend.
    
    Falls back to local JSON files if Firestore is unavailable.
    """
    
    COLLECTION_AI = "ai_analysis_cache"
    COLLECTION_MATCH = "match_analysis_cache"
    LOCAL_CACHE_DIR = "data/cache/ai_analysis"
    
    def __init__(self):
        self.db = get_firestore_client()
        self.use_firestore = self.db is not None
        
        # Ensure local cache dir exists for fallback
        os.makedirs(self.LOCAL_CACHE_DIR, exist_ok=True)
        
        if self.use_firestore:
            logger.info("Using Firestore for caching")
        else:
            logger.info("Using local file cache (Firestore unavailable)")
    
    # ============== AI Analysis Cache ==============
    
    def get_ai_analysis(self, date: str, league: str) -> Optional[Dict[str, Any]]:
        """
        Load cached AI analysis for a date/league.
        
        Args:
            date: Date string (YYYY-MM-DD)
            league: League name
            
        Returns:
            Dict mapping match_id to analysis dict, or None if not found
        """
        doc_id = self._make_doc_id(date, league)
        
        if self.use_firestore:
            return self._get_from_firestore(self.COLLECTION_AI, doc_id)
        else:
            return self._get_from_file(doc_id)
    
    def save_ai_analysis(self, date: str, league: str, data: Dict[str, Any]) -> bool:
        """
        Save AI analysis to cache.
        
        Args:
            date: Date string (YYYY-MM-DD)
            league: League name
            data: Dict mapping match_id to analysis dict
            
        Returns:
            True if saved successfully
        """
        doc_id = self._make_doc_id(date, league)
        
        # Add metadata
        cache_data = {
            "date": date,
            "league": league,
            "analyses": data,
            "created_at": datetime.utcnow().isoformat(),
            "match_count": len(data),
        }
        
        if self.use_firestore:
            return self._save_to_firestore(self.COLLECTION_AI, doc_id, cache_data)
        else:
            return self._save_to_file(doc_id, cache_data)
    
    # ============== Full Match Analysis Cache ==============
    
    def get_match_analysis(self, match_id: str) -> Optional[Dict[str, Any]]:
        """Load cached full match analysis."""
        if self.use_firestore:
            return self._get_from_firestore(self.COLLECTION_MATCH, match_id)
        else:
            return self._get_from_file(f"match_{match_id}")
    
    def save_match_analysis(self, match_id: str, data: Dict[str, Any]) -> bool:
        """Save full match analysis (stats + AI)."""
        cache_data = {
            **data,
            "cached_at": datetime.utcnow().isoformat(),
        }
        
        if self.use_firestore:
            return self._save_to_firestore(self.COLLECTION_MATCH, match_id, cache_data)
        else:
            return self._save_to_file(f"match_{match_id}", cache_data)
    
    def get_all_match_analyses(self, date: Optional[str] = None) -> Dict[str, Dict[str, Any]]:
        """Get all cached match analyses, optionally filtered by date."""
        if self.use_firestore:
            try:
                collection = self.db.collection(self.COLLECTION_MATCH)
                if date:
                    query = collection.where("date", "==", date)
                    docs = query.stream()
                else:
                    docs = collection.stream()
                
                return {doc.id: doc.to_dict() for doc in docs}
            except Exception as e:
                logger.error(f"Failed to get match analyses: {e}")
                return {}
        else:
            # File fallback - list all match files
            results = {}
            prefix = f"match_"
            for filename in os.listdir(self.LOCAL_CACHE_DIR):
                if filename.startswith(prefix):
                    filepath = os.path.join(self.LOCAL_CACHE_DIR, filename)
                    try:
                        with open(filepath, 'r') as f:
                            data = json.load(f)
                            if date is None or data.get("date") == date:
                                match_id = filename.replace(prefix, "").replace(".json", "")
                                results[match_id] = data
                    except:
                        pass
            return results
    
    # ============== Internal Methods ==============
    
    def _make_doc_id(self, date: str, league: str) -> str:
        """Create document ID from date and league."""
        safe_league = league.replace(" ", "_").replace("/", "_").lower()
        return f"{date}_{safe_league}"
    
    def _get_from_firestore(self, collection: str, doc_id: str) -> Optional[Dict]:
        """Get document from Firestore."""
        try:
            doc = self.db.collection(collection).document(doc_id).get()
            if doc.exists:
                logger.debug(f"Cache hit: {collection}/{doc_id}")
                return doc.to_dict()
            return None
        except Exception as e:
            logger.error(f"Firestore read error: {e}")
            return None
    
    def _save_to_firestore(self, collection: str, doc_id: str, data: Dict) -> bool:
        """Save document to Firestore."""
        try:
            self.db.collection(collection).document(doc_id).set(data)
            logger.debug(f"Saved to Firestore: {collection}/{doc_id}")
            return True
        except Exception as e:
            logger.error(f"Firestore write error: {e}")
            return False
    
    def _get_from_file(self, doc_id: str) -> Optional[Dict]:
        """Get from local file cache."""
        filepath = os.path.join(self.LOCAL_CACHE_DIR, f"{doc_id}.json")
        try:
            if os.path.exists(filepath):
                with open(filepath, 'r') as f:
                    return json.load(f)
        except Exception as e:
            logger.warning(f"File cache read error: {e}")
        return None
    
    def _save_to_file(self, doc_id: str, data: Dict) -> bool:
        """Save to local file cache."""
        filepath = os.path.join(self.LOCAL_CACHE_DIR, f"{doc_id}.json")
        try:
            with open(filepath, 'w') as f:
                json.dump(data, f, indent=2)
            return True
        except Exception as e:
            logger.error(f"File cache write error: {e}")
            return False


# Singleton instance
_cache_instance = None


def get_cache() -> FirestoreCache:
    """Get singleton cache instance."""
    global _cache_instance
    if _cache_instance is None:
        _cache_instance = FirestoreCache()
    return _cache_instance
