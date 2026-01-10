"""
SofaScore Fixture Service.

Fetches upcoming football fixtures from SofaScore internal API.
Outputs daily CSV files matching fixtures_clean.csv structure.
"""
import os
import csv
import requests
from datetime import date, datetime, timedelta
from typing import List, Dict, Any, Optional
from dataclasses import dataclass

from src.utils.logger import get_logger

logger = get_logger("SofaScoreFixtureService")


# SofaScore Tournament IDs for supported leagues
TOURNAMENT_IDS = {
    "E0": 17,     # Premier League
    "E1": 18,     # Championship
    "E2": 19,     # League One
    "E3": 20,     # League Two
    "D1": 35,     # Bundesliga
    "SP1": 8,     # La Liga
    "I1": 23,     # Serie A
    "I2": 53,     # Serie B
    "F1": 34,     # Ligue 1
    "F2": 182,    # Ligue 2
}

# Reverse map for lookup
TOURNAMENT_TO_DIV = {v: k for k, v in TOURNAMENT_IDS.items()}


@dataclass
class Fixture:
    """Fixture data matching fixtures_clean.csv structure."""
    div: str
    date: str
    time: str
    home_team: str
    away_team: str
    odds_home: float = 0.0
    odds_draw: float = 0.0
    odds_away: float = 0.0
    odds_over25: float = 0.0
    odds_under25: float = 0.0


class SofaScoreFixtureService:
    """
    Service to fetch fixtures from SofaScore.
    
    Uses the internal SofaScore API to get scheduled events.
    """
    
    BASE_URL = "https://www.sofascore.com/api/v1"
    OUTPUT_DIR = "data/raw/upcoming/daily"
    
    HEADERS = {
        "User-Agent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36",
        "Accept": "application/json",
        "Accept-Language": "en-US,en;q=0.9",
    }
    
    def __init__(self):
        os.makedirs(self.OUTPUT_DIR, exist_ok=True)
    
    def fetch_fixtures_for_date(self, target_date: date) -> List[Fixture]:
        """
        Fetch all fixtures for a specific date across supported leagues.
        
        Args:
            target_date: Date to fetch fixtures for
            
        Returns:
            List of Fixture objects
        """
        date_str = target_date.strftime("%Y-%m-%d")
        logger.info(f"Fetching fixtures for {date_str}")
        
        fixtures = []
        
        try:
            # SofaScore scheduled events endpoint
            url = f"{self.BASE_URL}/sport/football/scheduled-events/{date_str}"
            
            response = requests.get(url, headers=self.HEADERS, timeout=30)
            response.raise_for_status()
            
            data = response.json()
            events = data.get("events", [])
            
            logger.info(f"Found {len(events)} total events for {date_str}")
            
            for event in events:
                fixture = self._parse_event(event)
                if fixture:
                    fixtures.append(fixture)
            
            logger.info(f"Extracted {len(fixtures)} fixtures for supported leagues")
            
        except requests.RequestException as e:
            logger.error(f"Failed to fetch fixtures for {date_str}: {e}")
        except Exception as e:
            logger.error(f"Error processing fixtures for {date_str}: {e}")
            
        return fixtures
    
    def _parse_event(self, event: Dict[str, Any]) -> Optional[Fixture]:
        """Parse a SofaScore event into a Fixture object."""
        try:
            # Extract tournament info
            tournament = event.get("tournament", {})
            tournament_id = tournament.get("uniqueTournament", {}).get("id")
            
            # Check if supported league
            div = TOURNAMENT_TO_DIV.get(tournament_id)
            if not div:
                return None
            
            # Extract teams
            home_team = event.get("homeTeam", {}).get("name", "Unknown")
            away_team = event.get("awayTeam", {}).get("name", "Unknown")
            
            # Extract time
            start_timestamp = event.get("startTimestamp", 0)
            start_dt = datetime.fromtimestamp(start_timestamp)
            
            date_str = start_dt.strftime("%Y-%m-%d")
            time_str = start_dt.strftime("%H:%M")
            
            # Try to get odds (may not be available)
            odds = self._fetch_odds(event.get("id"))
            
            return Fixture(
                div=div,
                date=date_str,
                time=time_str,
                home_team=home_team,
                away_team=away_team,
                odds_home=odds.get("home", 0.0),
                odds_draw=odds.get("draw", 0.0),
                odds_away=odds.get("away", 0.0),
                odds_over25=odds.get("over25", 0.0),
                odds_under25=odds.get("under25", 0.0),
            )
            
        except Exception as e:
            logger.warning(f"Failed to parse event: {e}")
            return None
    
    def _fetch_odds(self, event_id: Optional[int]) -> Dict[str, float]:
        """Fetch odds for a specific event."""
        if not event_id:
            return {}
            
        try:
            url = f"{self.BASE_URL}/event/{event_id}/odds/1/all"
            response = requests.get(url, headers=self.HEADERS, timeout=10)
            
            if response.status_code != 200:
                return {}
            
            data = response.json()
            markets = data.get("markets", [])
            
            odds = {}
            
            for market in markets:
                market_name = market.get("marketName", "")
                choices = market.get("choices", [])
                
                if market_name == "Full time":
                    for choice in choices:
                        name = choice.get("name", "")
                        fractional = choice.get("fractionalValue", "1/1")
                        decimal_odd = self._fractional_to_decimal(fractional)
                        
                        if name == "1":
                            odds["home"] = decimal_odd
                        elif name == "X":
                            odds["draw"] = decimal_odd
                        elif name == "2":
                            odds["away"] = decimal_odd
                            
                elif market_name == "Over/Under":
                    for choice in choices:
                        name = choice.get("name", "")
                        fractional = choice.get("fractionalValue", "1/1")
                        decimal_odd = self._fractional_to_decimal(fractional)
                        
                        if "Over 2.5" in name:
                            odds["over25"] = decimal_odd
                        elif "Under 2.5" in name:
                            odds["under25"] = decimal_odd
            
            return odds
            
        except Exception as e:
            logger.debug(f"Could not fetch odds for event {event_id}: {e}")
            return {}
    
    def _fractional_to_decimal(self, fractional: str) -> float:
        """Convert fractional odds to decimal."""
        try:
            parts = fractional.split("/")
            if len(parts) == 2:
                return round(float(parts[0]) / float(parts[1]) + 1, 2)
            return float(fractional)
        except:
            return 0.0
    
    def save_fixtures_csv(self, fixtures: List[Fixture], target_date: date) -> str:
        """
        Save fixtures to CSV file.
        
        Args:
            fixtures: List of Fixture objects
            target_date: Date for filename
            
        Returns:
            Path to saved CSV file
        """
        date_str = target_date.strftime("%Y-%m-%d")
        filename = f"fixtures_{date_str}.csv"
        filepath = os.path.join(self.OUTPUT_DIR, filename)
        
        with open(filepath, 'w', newline='', encoding='utf-8') as f:
            writer = csv.writer(f)
            
            # Header matching fixtures_clean.csv
            writer.writerow([
                "Div", "Date", "Time", "HomeTeam", "AwayTeam",
                "B365H", "B365D", "B365A", "B365>2.5", "B365<2.5"
            ])
            
            for fix in fixtures:
                writer.writerow([
                    fix.div,
                    fix.date,
                    fix.time,
                    fix.home_team,
                    fix.away_team,
                    fix.odds_home or "",
                    fix.odds_draw or "",
                    fix.odds_away or "",
                    fix.odds_over25 or "",
                    fix.odds_under25 or "",
                ])
        
        logger.info(f"Saved {len(fixtures)} fixtures to {filepath}")
        return filepath
    
    def fetch_date_range(
        self, 
        start_date: date, 
        end_date: date
    ) -> Dict[str, str]:
        """
        Fetch fixtures for a date range and save to separate CSV files.
        
        Args:
            start_date: Start of date range
            end_date: End of date range (inclusive)
            
        Returns:
            Dict mapping date string to filepath
        """
        results = {}
        current = start_date
        
        while current <= end_date:
            fixtures = self.fetch_fixtures_for_date(current)
            
            if fixtures:
                filepath = self.save_fixtures_csv(fixtures, current)
                results[current.strftime("%Y-%m-%d")] = filepath
            else:
                logger.warning(f"No fixtures found for {current}")
            
            current += timedelta(days=1)
        
        logger.info(f"Fetched fixtures for {len(results)} days")
        return results


# Convenience function for scripts
def fetch_january_fixtures():
    """Fetch fixtures for January 1-12, 2026."""
    service = SofaScoreFixtureService()
    
    start = date(2026, 1, 1)
    end = date(2026, 1, 12)
    
    return service.fetch_date_range(start, end)


if __name__ == "__main__":
    # Run if executed directly
    results = fetch_january_fixtures()
    print(f"Created {len(results)} fixture files")
    for date_str, path in results.items():
        print(f"  {date_str}: {path}")
