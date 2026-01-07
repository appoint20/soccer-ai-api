"""
Data Health Service.

Startup validation of historical data to detect stale or incomplete data.
"""
from dataclasses import dataclass, field
from datetime import date, datetime, timedelta
from typing import List, Dict, Any, Optional
from pathlib import Path

from src.utils.logger import get_logger


@dataclass
class LeagueHealth:
    """Health status for a single league."""
    league_code: str
    league_name: str
    match_count: int
    last_match_date: Optional[date]
    days_since_last: int
    is_stale: bool
    missing_odds_pct: float


@dataclass
class DataHealthReport:
    """Overall data health report."""
    check_date: date
    total_matches: int
    leagues_checked: int
    healthy_leagues: int
    stale_leagues: int
    warnings: List[str] = field(default_factory=list)
    league_details: List[LeagueHealth] = field(default_factory=list)
    is_healthy: bool = True
    
    def to_dict(self) -> Dict[str, Any]:
        """Convert to dictionary for logging/API."""
        return {
            "check_date": self.check_date.isoformat(),
            "total_matches": self.total_matches,
            "leagues_checked": self.leagues_checked,
            "healthy_leagues": self.healthy_leagues,
            "stale_leagues": self.stale_leagues,
            "is_healthy": self.is_healthy,
            "warnings": self.warnings,
            "league_details": [
                {
                    "league_code": ld.league_code,
                    "league_name": ld.league_name,
                    "match_count": ld.match_count,
                    "last_match_date": ld.last_match_date.isoformat() if ld.last_match_date else None,
                    "days_since_last": ld.days_since_last,
                    "is_stale": ld.is_stale,
                    "missing_odds_pct": round(ld.missing_odds_pct, 1),
                }
                for ld in self.league_details
            ],
        }


LEAGUE_NAMES = {
    "E0": "Premier League",
    "E1": "Championship",
    "E2": "League One",
    "E3": "League Two",
    "D1": "Bundesliga",
    "SP1": "La Liga",
    "I1": "Serie A",
    "I2": "Serie B",
    "F1": "Ligue 1",
    "F2": "Ligue 2",
}


class DataHealthService:
    """
    Startup validation of historical data.
    
    Checks:
    - Last match date per league
    - Match count per league
    - Missing odds detection
    - Stale data warnings
    """
    
    STALE_THRESHOLD_DAYS = 7  # Data older than this is considered stale
    MIN_MATCHES_PER_LEAGUE = 50  # Alert if fewer matches than this
    
    def __init__(
        self,
        stale_threshold_days: int = 7,
        min_matches_per_league: int = 50,
    ):
        """
        Initialize service.
        
        Args:
            stale_threshold_days: Days without data before warning
            min_matches_per_league: Minimum matches expected per league
        """
        self.logger = get_logger("DataHealthService")
        self.stale_threshold_days = stale_threshold_days
        self.min_matches_per_league = min_matches_per_league
    
    def validate_on_startup(
        self,
        historical_matches: List[Dict[str, Any]],
        supported_leagues: Optional[List[str]] = None,
    ) -> DataHealthReport:
        """
        Run health check on historical data.
        
        Args:
            historical_matches: List of historical match dictionaries
            supported_leagues: Leagues to check (defaults to all)
            
        Returns:
            DataHealthReport with status and warnings
        """
        today = date.today()
        warnings = []
        
        if not historical_matches:
            self.logger.error("No historical matches provided for health check!")
            return DataHealthReport(
                check_date=today,
                total_matches=0,
                leagues_checked=0,
                healthy_leagues=0,
                stale_leagues=0,
                warnings=["No historical data available!"],
                is_healthy=False,
            )
        
        # Group matches by league
        leagues_data = self._group_by_league(historical_matches)
        
        # Filter to supported leagues if specified
        if supported_leagues:
            leagues_data = {
                k: v for k, v in leagues_data.items()
                if k in supported_leagues
            }
        
        # Analyze each league
        league_health = []
        healthy_count = 0
        stale_count = 0
        
        for league_code, matches in leagues_data.items():
            health = self._analyze_league(league_code, matches, today)
            league_health.append(health)
            
            if health.is_stale:
                stale_count += 1
                warnings.append(
                    f"{health.league_name} ({league_code}): Data is {health.days_since_last} days old"
                )
            else:
                healthy_count += 1
            
            if health.match_count < self.min_matches_per_league:
                warnings.append(
                    f"{health.league_name}: Only {health.match_count} matches (expected ≥{self.min_matches_per_league})"
                )
            
            if health.missing_odds_pct > 30:
                warnings.append(
                    f"{health.league_name}: {health.missing_odds_pct:.0f}% missing odds data"
                )
        
        # Overall health
        is_healthy = stale_count == 0 and len(warnings) == 0
        
        report = DataHealthReport(
            check_date=today,
            total_matches=len(historical_matches),
            leagues_checked=len(league_health),
            healthy_leagues=healthy_count,
            stale_leagues=stale_count,
            warnings=warnings,
            league_details=league_health,
            is_healthy=is_healthy,
        )
        
        # Log report
        if is_healthy:
            self.logger.info(
                f"✅ Data health check passed: {report.total_matches} matches across {report.leagues_checked} leagues"
            )
        else:
            self.logger.warning(f"⚠️ Data health issues detected:")
            for warning in warnings:
                self.logger.warning(f"  - {warning}")
        
        return report
    
    def _group_by_league(
        self,
        matches: List[Dict[str, Any]],
    ) -> Dict[str, List[Dict[str, Any]]]:
        """Group matches by league code."""
        grouped = {}
        
        for match in matches:
            league = match.get("Div") or match.get("league") or match.get("league_code") or "UNKNOWN"
            
            if league not in grouped:
                grouped[league] = []
            grouped[league].append(match)
        
        return grouped
    
    def _analyze_league(
        self,
        league_code: str,
        matches: List[Dict[str, Any]],
        reference_date: date,
    ) -> LeagueHealth:
        """Analyze health of a single league's data."""
        league_name = LEAGUE_NAMES.get(league_code, league_code)
        
        # Find last match date
        last_date = None
        missing_odds_count = 0
        
        for match in matches:
            # Parse date
            match_date = self._parse_date(match)
            if match_date and (last_date is None or match_date > last_date):
                last_date = match_date
            
            # Check for missing odds
            odds_fields = ["B365H", "B365D", "B365A", "AvgH", "AvgD", "AvgA"]
            has_odds = any(match.get(f) for f in odds_fields)
            if not has_odds:
                missing_odds_count += 1
        
        # Calculate metrics
        days_since = (reference_date - last_date).days if last_date else 999
        is_stale = days_since > self.stale_threshold_days
        missing_odds_pct = (missing_odds_count / len(matches) * 100) if matches else 0
        
        return LeagueHealth(
            league_code=league_code,
            league_name=league_name,
            match_count=len(matches),
            last_match_date=last_date,
            days_since_last=days_since,
            is_stale=is_stale,
            missing_odds_pct=missing_odds_pct,
        )
    
    def _parse_date(self, match: Dict[str, Any]) -> Optional[date]:
        """Parse match date from various formats."""
        date_val = match.get("Date") or match.get("date") or match.get("parsed_date")
        
        if date_val is None:
            return None
        
        if isinstance(date_val, date):
            return date_val
        
        if isinstance(date_val, datetime):
            return date_val.date()
        
        if isinstance(date_val, str):
            for fmt in ["%Y-%m-%d", "%d/%m/%Y", "%d/%m/%y"]:
                try:
                    return datetime.strptime(date_val[:10], fmt).date()
                except ValueError:
                    continue
        
        return None
