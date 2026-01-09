"""
Matchday Calculator.

Calculates the matchday (round) for a league match.

Rules:
- Group matches by league_code + season
- Sort by date
- Assign incremental matchday numbers
- Handle multiple matches on same date (same round)
"""
from datetime import date, datetime
from typing import Dict, List, Optional, Tuple
from collections import defaultdict

from src.utils.logger import get_logger

logger = get_logger("MatchdayCalculator")


class MatchdayCalculator:
    """
    Calculates matchday/round numbers for league matches.
    
    A matchday typically includes all matches in a single round,
    which may span 2-3 days (Friday-Sunday).
    """
    
    def __init__(self, round_window_days: int = 4):
        """
        Initialize calculator.
        
        Args:
            round_window_days: Days within which matches are same round
        """
        self.round_window_days = round_window_days
        self._cache: Dict[str, Dict[str, int]] = {}  # league+season -> date_key -> matchday
    
    def calculate_matchday(
        self,
        match_date: date,
        league_code: str,
        season: str,
        historical_matches: List[Dict],
    ) -> int:
        """
        Calculate matchday for a specific match.
        
        Args:
            match_date: Date of the match
            league_code: League identifier (E0, D1, etc.)
            season: Season identifier (2024-2025)
            historical_matches: All matches for this league/season
            
        Returns:
            Matchday number (1-38 typically)
        """
        cache_key = f"{league_code}_{season}"
        
        # Build cache if not exists
        if cache_key not in self._cache:
            self._build_matchday_cache(league_code, season, historical_matches)
        
        # Look up matchday
        date_key = match_date.isoformat() if isinstance(match_date, date) else str(match_date)[:10]
        
        if cache_key in self._cache and date_key in self._cache[cache_key]:
            return self._cache[cache_key][date_key]
        
        # If match date not in history, estimate next matchday
        if cache_key in self._cache:
            existing_matchdays = list(self._cache[cache_key].values())
            return max(existing_matchdays) + 1 if existing_matchdays else 1
        
        return 1
    
    def get_current_matchday(
        self,
        league_code: str,
        season: str,
        historical_matches: List[Dict],
        as_of_date: Optional[date] = None,
    ) -> int:
        """
        Get the current matchday for a league.
        
        Args:
            league_code: League identifier
            season: Season identifier
            historical_matches: Match history
            as_of_date: Reference date (default: today)
            
        Returns:
            Current matchday number
        """
        cache_key = f"{league_code}_{season}"
        
        if cache_key not in self._cache:
            self._build_matchday_cache(league_code, season, historical_matches)
        
        if as_of_date is None:
            as_of_date = date.today()
        
        # Find last completed matchday
        if cache_key not in self._cache:
            return 1
        
        last_matchday = 0
        for date_key, matchday in self._cache[cache_key].items():
            match_date = datetime.fromisoformat(date_key).date()
            if match_date <= as_of_date:
                last_matchday = max(last_matchday, matchday)
        
        return last_matchday
    
    def _build_matchday_cache(
        self,
        league_code: str,
        season: str,
        matches: List[Dict],
    ) -> None:
        """Build matchday mapping from historical matches."""
        cache_key = f"{league_code}_{season}"
        self._cache[cache_key] = {}
        
        # Filter matches for this league/season
        league_matches = [
            m for m in matches
            if self._get_league(m) == league_code
            and self._get_season(m) == season
            and self._get_date(m) is not None
        ]
        
        if not league_matches:
            return
        
        # Sort by date
        league_matches.sort(key=lambda m: self._get_date(m))
        
        # Group into matchdays
        current_matchday = 0
        last_round_start: Optional[date] = None
        
        for match in league_matches:
            match_date = self._get_date(match)
            date_key = match_date.isoformat()
            
            # Check if this is a new round
            if last_round_start is None:
                current_matchday = 1
                last_round_start = match_date
            elif (match_date - last_round_start).days > self.round_window_days:
                current_matchday += 1
                last_round_start = match_date
            
            self._cache[cache_key][date_key] = current_matchday
    
    def _get_date(self, match: Dict) -> Optional[date]:
        """Extract date from match dict."""
        for key in ["date", "match_date", "Date"]:
            if key in match and match[key]:
                val = match[key]
                if isinstance(val, date):
                    return val
                if isinstance(val, datetime):
                    return val.date()
                if isinstance(val, str):
                    try:
                        return datetime.fromisoformat(val[:10]).date()
                    except:
                        pass
        return None
    
    def _get_league(self, match: Dict) -> str:
        """Extract league code from match dict."""
        return match.get("league") or match.get("Div") or match.get("league_code") or ""
    
    def _get_season(self, match: Dict) -> str:
        """Extract season from match dict."""
        return match.get("season") or match.get("Season") or ""
