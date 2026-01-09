"""
API-Football HTTP Client.

Handles all HTTP communication with the API-Football API.
"""
import asyncio
from typing import Any, Dict, List, Optional
from datetime import datetime, timedelta

import httpx

from src.core.cache_manager import get_cache_manager, CacheManager
from src.core.rate_limiter import get_rate_limiter, RateLimiter, RateLimitExceeded
from src.utils.logger import get_logger

logger = get_logger("APIFootballClient")


class APIFootballClient:
    """
    Async HTTP client for API-Football.
    
    Features:
    - Automatic caching
    - Rate limiting (100 calls/day)
    - Concurrent odds fetching
    - Error handling
    """
    
    BASE_URL = "https://v3.football.api-sports.io"
    BOOKMAKER_BET365 = 8
    
    # Supported leagues
    LEAGUES = {
        "premier_league": 39,
        "championship": 40,  # English Championship
        "la_liga": 140,
        "bundesliga": 78,
        "serie_a": 135,
        "serie_b": 136,
        "ligue_1": 61,
        "league_one": 41,
        "league_two": 42,
    }
    
    def __init__(
        self,
        api_key: str,
        cache: Optional[CacheManager] = None,
        rate_limiter: Optional[RateLimiter] = None,
    ):
        self._api_key = api_key
        self._cache = cache or get_cache_manager()
        self._rate_limiter = rate_limiter or get_rate_limiter()
        self._client = httpx.AsyncClient(
            base_url=self.BASE_URL,
            headers={
                "x-rapidapi-key": api_key,
                "x-rapidapi-host": "v3.football.api-sports.io",
            },
            timeout=30.0,
        )
    
    async def close(self):
        """Close the HTTP client."""
        await self._client.aclose()
    
    async def get_fixtures(
        self,
        league: str,
        days_ahead: int = 7,
    ) -> Dict[str, Any]:
        """
        Get upcoming fixtures for a league.
        
        Args:
            league: League key (e.g., "premier_league")
            days_ahead: Number of days to look ahead (1-30)
            
        Returns:
            Dict with league info and fixtures
        """
        if league not in self.LEAGUES:
            raise ValueError(f"Unknown league: {league}. Valid: {list(self.LEAGUES.keys())}")
        
        league_id = self.LEAGUES[league]
        season = self._get_current_season()
        
        from_date = datetime.now().strftime("%Y-%m-%d")
        to_date = (datetime.now() + timedelta(days=days_ahead)).strftime("%Y-%m-%d")
        
        # Check cache first
        cached = await self._cache.get_fixtures(league_id, from_date, to_date)
        if cached:
            logger.info(f"Returning cached fixtures for {league}")
            return cached
        
        # Make API call
        await self._rate_limiter.record_call()
        
        response = await self._client.get(
            "/fixtures",
            params={
                "league": league_id,
                "season": season,
                "from": from_date,
                "to": to_date,
            },
        )
        response.raise_for_status()
        data = response.json()
        
        fixtures = data.get("response", [])
        logger.info(f"Fetched {len(fixtures)} fixtures for {league}")
        
        # Fetch odds for each fixture (concurrently in batches)
        fixtures_with_odds = await self._fetch_odds_for_fixtures(fixtures)
        
        result = {
            "league": league,
            "league_id": league_id,
            "total_fixtures": len(fixtures_with_odds),
            "from_date": from_date,
            "to_date": to_date,
            "fixtures": fixtures_with_odds,
        }
        
        # Cache the result
        await self._cache.set_fixtures(league_id, from_date, to_date, result)
        
        return result
    
    async def get_odds(self, fixture_id: int) -> Optional[Dict[str, Any]]:
        """
        Get Bet365 odds for a fixture.
        
        Args:
            fixture_id: Fixture ID
            
        Returns:
            Dict with Bet365 odds or None if not available
        """
        # Check cache first
        cached = await self._cache.get_odds(fixture_id)
        if cached:
            return cached
        
        try:
            await self._rate_limiter.record_call()
            
            response = await self._client.get(
                "/odds",
                params={
                    "fixture": fixture_id,
                    "bookmaker": self.BOOKMAKER_BET365,
                },
            )
            response.raise_for_status()
            data = response.json()
            
            odds = self._parse_odds(data.get("response", []))
            
            if odds:
                await self._cache.set_odds(fixture_id, odds)
            
            return odds
            
        except RateLimitExceeded:
            logger.warning(f"Rate limit reached, skipping odds for fixture {fixture_id}")
            return None
        except Exception as e:
            logger.error(f"Failed to fetch odds for fixture {fixture_id}: {e}")
            return None
    
    async def _fetch_odds_for_fixtures(
        self,
        fixtures: List[Dict],
        batch_size: int = 5,
    ) -> List[Dict]:
        """Fetch odds for multiple fixtures concurrently."""
        result = []
        
        for i in range(0, len(fixtures), batch_size):
            batch = fixtures[i:i + batch_size]
            
            # Fetch odds concurrently for this batch
            tasks = []
            for fixture in batch:
                fixture_id = fixture.get("fixture", {}).get("id")
                if fixture_id:
                    tasks.append(self._get_fixture_with_odds(fixture, fixture_id))
                else:
                    result.append(self._format_fixture(fixture, None))
            
            if tasks:
                batch_results = await asyncio.gather(*tasks, return_exceptions=True)
                for res in batch_results:
                    if isinstance(res, Exception):
                        logger.error(f"Batch odds fetch error: {res}")
                    else:
                        result.append(res)
            
            # Delay between batches
            if i + batch_size < len(fixtures):
                await asyncio.sleep(1)
        
        return result
    
    async def _get_fixture_with_odds(self, fixture: Dict, fixture_id: int) -> Dict:
        """Get fixture formatted with odds."""
        odds = await self.get_odds(fixture_id)
        return self._format_fixture(fixture, odds)
    
    def _format_fixture(self, fixture: Dict, odds: Optional[Dict]) -> Dict:
        """Format fixture response."""
        f = fixture.get("fixture", {})
        league = fixture.get("league", {})
        teams = fixture.get("teams", {})
        
        return {
            "fixture_id": f.get("id"),
            "league_name": league.get("name"),
            "league_id": league.get("id"),
            "match_date": f.get("date"),
            "home_team": {
                "id": teams.get("home", {}).get("id"),
                "name": teams.get("home", {}).get("name"),
                "logo": teams.get("home", {}).get("logo"),
            },
            "away_team": {
                "id": teams.get("away", {}).get("id"),
                "name": teams.get("away", {}).get("name"),
                "logo": teams.get("away", {}).get("logo"),
            },
            "venue": f.get("venue", {}).get("name"),
            "bet365_odds": odds,
        }
    
    def _parse_odds(self, response: List) -> Optional[Dict]:
        """Parse Bet365 odds from API response."""
        if not response:
            return None
        
        odds_data = response[0] if response else {}
        bookmakers = odds_data.get("bookmakers", [])
        
        if not bookmakers:
            return None
        
        bet365 = bookmakers[0]  # Already filtered by bookmaker=8
        bets = bet365.get("bets", [])
        
        result = {
            "match_winner": None,
            "over_under": None,
            "btts": None,
            "goals_2_3": None,
        }
        
        for bet in bets:
            bet_name = bet.get("name", "")
            values = bet.get("values", [])
            
            if bet_name == "Match Winner":
                result["match_winner"] = self._parse_match_winner(values)
            elif bet_name == "Goals Over/Under":
                result["over_under"] = self._parse_over_under(values)
            elif bet_name == "Both Teams Score":
                result["btts"] = self._parse_btts(values)
        
        return result
    
    def _parse_match_winner(self, values: List) -> Dict:
        """Parse match winner odds."""
        odds = {"home": None, "draw": None, "away": None}
        for v in values:
            val = v.get("value")
            odd = float(v.get("odd", 0))
            if val == "Home":
                odds["home"] = odd
            elif val == "Draw":
                odds["draw"] = odd
            elif val == "Away":
                odds["away"] = odd
        return odds
    
    def _parse_over_under(self, values: List) -> Dict:
        """Parse over/under 2.5 odds."""
        odds = {"over_25": None, "under_25": None}
        for v in values:
            val = v.get("value", "")
            odd = float(v.get("odd", 0))
            if "Over 2.5" in val:
                odds["over_25"] = odd
            elif "Under 2.5" in val:
                odds["under_25"] = odd
        return odds
    
    def _parse_btts(self, values: List) -> Dict:
        """Parse BTTS odds."""
        odds = {"yes": None, "no": None}
        for v in values:
            val = v.get("value")
            odd = float(v.get("odd", 0))
            if val == "Yes":
                odds["yes"] = odd
            elif val == "No":
                odds["no"] = odd
        return odds
    
    def _get_current_season(self) -> int:
        """Get current football season year."""
        now = datetime.now()
        if now.month >= 8:
            return now.year
        return now.year - 1
