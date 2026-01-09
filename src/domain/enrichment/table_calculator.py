"""
Table Calculator.

Calculates live league standings from historical matches.
"""
from datetime import date, datetime
from typing import Dict, List, Optional
from collections import defaultdict

from src.domain.enrichment.models import TeamStanding
from src.utils.logger import get_logger

logger = get_logger("TableCalculator")


class TableCalculator:
    """
    Calculates league table standings.
    
    Supports:
    - Full table
    - Home-only table
    - Away-only table
    """
    
    def calculate_table(
        self,
        league_code: str,
        season: str,
        matches: List[Dict],
        before_date: Optional[date] = None,
        venue_filter: Optional[str] = None,
    ) -> List[TeamStanding]:
        """
        Calculate league table.
        
        Args:
            league_code: League identifier
            season: Season identifier
            matches: Historical matches
            before_date: Only include matches before this date
            venue_filter: "home" or "away" for venue-specific table
            
        Returns:
            Sorted list of TeamStanding (by points, GD, GF)
        """
        # Filter matches
        league_matches = self._filter_matches(
            matches, league_code, season, before_date
        )
        
        # Build team stats
        stats: Dict[str, Dict] = defaultdict(lambda: {
            "played": 0, "wins": 0, "draws": 0, "losses": 0,
            "goals_for": 0, "goals_against": 0, "points": 0,
            "form": [],
        })
        
        for match in league_matches:
            home_team = match.get("home_team") or match.get("HomeTeam") or ""
            away_team = match.get("away_team") or match.get("AwayTeam") or ""
            home_goals = self._get_goals(match, "home")
            away_goals = self._get_goals(match, "away")
            
            if not home_team or not away_team or home_goals is None or away_goals is None:
                continue
            
            # Apply venue filter
            if venue_filter == "home":
                self._update_stats(stats[home_team], home_goals, away_goals)
            elif venue_filter == "away":
                self._update_stats(stats[away_team], away_goals, home_goals)
            else:
                # Full table - both teams
                self._update_stats(stats[home_team], home_goals, away_goals)
                self._update_stats(stats[away_team], away_goals, home_goals)
        
        # Build standings
        standings = []
        for team, s in stats.items():
            standings.append(TeamStanding(
                position=0,  # Set after sorting
                team=team,
                played=s["played"],
                wins=s["wins"],
                draws=s["draws"],
                losses=s["losses"],
                goals_for=s["goals_for"],
                goals_against=s["goals_against"],
                goal_difference=s["goals_for"] - s["goals_against"],
                points=s["points"],
                form="".join(s["form"][-5:]),  # Last 5
            ))
        
        # Sort by points, GD, GF
        standings.sort(key=lambda x: (-x.points, -x.goal_difference, -x.goals_for))
        
        # Assign positions
        result = []
        for i, standing in enumerate(standings, 1):
            result.append(TeamStanding(
                position=i,
                team=standing.team,
                played=standing.played,
                wins=standing.wins,
                draws=standing.draws,
                losses=standing.losses,
                goals_for=standing.goals_for,
                goals_against=standing.goals_against,
                goal_difference=standing.goal_difference,
                points=standing.points,
                form=standing.form,
            ))
        
        return result
    
    def get_team_position(
        self,
        team: str,
        league_code: str,
        season: str,
        matches: List[Dict],
        before_date: Optional[date] = None,
    ) -> Optional[TeamStanding]:
        """Get a specific team's standing."""
        table = self.calculate_table(league_code, season, matches, before_date)
        team_lower = team.lower()
        
        for standing in table:
            if standing.team.lower() == team_lower:
                return standing
        
        return None
    
    def _filter_matches(
        self,
        matches: List[Dict],
        league_code: str,
        season: str,
        before_date: Optional[date],
    ) -> List[Dict]:
        """Filter matches by league, season, and date."""
        result = []
        
        for match in matches:
            league = match.get("league") or match.get("Div") or ""
            match_season = match.get("season") or match.get("Season") or ""
            
            if league != league_code or match_season != season:
                continue
            
            if before_date:
                match_date = self._get_date(match)
                if match_date and match_date >= before_date:
                    continue
            
            result.append(match)
        
        return result
    
    def _update_stats(self, stats: Dict, goals_for: int, goals_against: int):
        """Update team stats for a single match."""
        stats["played"] += 1
        stats["goals_for"] += goals_for
        stats["goals_against"] += goals_against
        
        if goals_for > goals_against:
            stats["wins"] += 1
            stats["points"] += 3
            stats["form"].append("W")
        elif goals_for < goals_against:
            stats["losses"] += 1
            stats["form"].append("L")
        else:
            stats["draws"] += 1
            stats["points"] += 1
            stats["form"].append("D")
    
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
