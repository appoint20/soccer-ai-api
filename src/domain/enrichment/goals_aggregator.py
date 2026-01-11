"""
Goals Aggregator.

Calculates season goals statistics for teams.
"""
from datetime import date, datetime
from typing import Dict, List, Optional

from src.domain.enrichment.models import GoalsStats
from src.utils.logger import get_logger

logger = get_logger("GoalsAggregator")


class GoalsAggregator:
    """
    Aggregates goals statistics for a team.
    
    Calculates:
    - Total scored/conceded
    - Home-only totals
    - Away-only totals
    """
    
    
    def __init__(self, team_matcher=None):
        """
        Initialize calculator.
        
        Args:
            team_matcher: Optional TeamNameMatcher for fuzzy matching
        """
        self.matcher = team_matcher

    def calculate_goals_stats(
        self,
        team: str,
        league_code: str,
        season: str,
        matches: List[Dict],
        before_date: Optional[date] = None,
    ) -> GoalsStats:
        """
        Calculate goals statistics for a team.
        
        Args:
            team: Team name
            league_code: League identifier
            season: Season identifier
            matches: Historical matches
            before_date: Only include matches before this date
            
        Returns:
            GoalsStats with all totals
        """
        team_lower = team.lower()
        
        total_scored = 0
        total_conceded = 0
        home_scored = 0
        home_conceded = 0
        away_scored = 0
        away_conceded = 0
        home_matches = 0
        away_matches = 0
        
        for match in matches:
            # Filter by league/season
            league = match.get("league") or match.get("Div") or ""
            match_season = match.get("season") or match.get("Season") or ""
            
            if league != league_code or match_season != season:
                continue
            
            # Filter by date
            if before_date:
                match_date = self._get_date(match)
                if match_date and match_date >= before_date:
                    continue
            
            home_team = (match.get("home_team") or match.get("HomeTeam") or "").lower()
            away_team = (match.get("away_team") or match.get("AwayTeam") or "").lower()
            
            home_goals = self._get_goals(match, "home")
            away_goals = self._get_goals(match, "away")
            
            if home_goals is None or away_goals is None:
                continue
            
            # Check team match
            is_home = False
            is_away = False
            
            if self.matcher:
                is_home = self.matcher.matches(home_team, team)
                is_away = self.matcher.matches(away_team, team)
            else:
                is_home = home_team == team_lower
                is_away = away_team == team_lower
            
            if is_home:
                total_scored += home_goals
                total_conceded += away_goals
                home_scored += home_goals
                home_conceded += away_goals
                home_matches += 1
            elif is_away:
                total_scored += away_goals
                total_conceded += home_goals
                away_scored += away_goals
                away_conceded += home_goals
                away_matches += 1
        
        return GoalsStats(
            total_scored=total_scored,
            total_conceded=total_conceded,
            home_scored=home_scored,
            home_conceded=home_conceded,
            away_scored=away_scored,
            away_conceded=away_conceded,
            matches_played=home_matches + away_matches,
            home_matches=home_matches,
            away_matches=away_matches,
        )
    
    def _get_date(self, match: Dict) -> Optional[date]:
        """Extract date from match."""
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
    
    def _get_goals(self, match: Dict, side: str) -> Optional[int]:
        """Get goals for home or away."""
        if side == "home":
            keys = ["fthg", "FTHG", "home_goals"]
        else:
            keys = ["ftag", "FTAG", "away_goals"]
        
        for key in keys:
            if key in match and match[key] is not None:
                try:
                    return int(match[key])
                except:
                    pass
        return None
