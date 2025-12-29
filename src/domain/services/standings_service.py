"""
Standings service for tracking league positions over time.

Calculates league tables, position changes, and contextual features
like distance from top 4 or relegation zone.
"""
from datetime import date, datetime
from typing import Optional, List, Dict, Any
from collections import defaultdict

from src.domain.services.base_service import BaseService
from src.data.cache.cache_manager import CacheManager
from src.utils.stats_utils import round_to_precision
from src.utils.date_utils import get_season_from_date


class StandingsService(BaseService):
    """
    Track and calculate league standings over time.
    
    Features:
    - Complete standings at any date
    - Position tracking for teams
    - Form tables (last N matches)
    - Contextual features (top 4, relegation, etc.)
    """
    
    def __init__(
        self,
        cache_manager: Optional[CacheManager] = None,
        top_positions: int = 4,  # For Champions League spots
        relegation_positions: int = 3,  # Bottom positions
    ):
        """
        Initialize standings service.
        
        Args:
            cache_manager: Optional cache manager
            top_positions: Number of top positions to track
            relegation_positions: Number of relegation positions
        """
        super().__init__(cache_manager)
        self.top_positions = top_positions
        self.relegation_positions = relegation_positions
    
    def build_standings_timeline(
        self,
        matches: List,
        league: str,
        season: Optional[str] = None,
    ) -> Dict[str, Dict[str, Any]]:
        """
        Build complete standings history for a league.
        
        Args:
            matches: All matches for the league
            league: League code
            season: Optional season filter
            
        Returns:
            Dict mapping dates to standings
        """
        cache_key = self.generate_cache_key("timeline", league, season or "all")
        cached = self.get_cached(cache_key)
        if cached is not None:
            return cached
        
        with self.track_performance("build_standings_timeline"):
            # Filter matches for league
            league_matches = [
                m for m in matches
                if self._get_match_field(m, "league") == league
            ]
            
            if season:
                league_matches = [
                    m for m in league_matches
                    if self._get_match_field(m, "season") == season
                ]
            
            if not league_matches:
                return {}
            
            # Sort by date
            league_matches.sort(
                key=lambda m: self._get_match_field(m, "match_date") or date.min
            )
            
            # Build cumulative standings after each matchday
            timeline = {}
            team_records = defaultdict(lambda: {
                "played": 0, "won": 0, "drawn": 0, "lost": 0,
                "goals_for": 0, "goals_against": 0, "points": 0,
            })
            
            current_date = None
            
            for match in league_matches:
                match_date = self._get_match_field(match, "match_date")
                if not match_date:
                    continue
                
                if isinstance(match_date, str):
                    try:
                        match_date = datetime.fromisoformat(match_date[:10]).date()
                    except (ValueError, TypeError):
                        continue
                
                # Update team records
                self._update_records_from_match(team_records, match)
                
                # Store standings for this date
                date_str = str(match_date)[:10]
                timeline[date_str] = {
                    "standings": self._records_to_standings(team_records),
                }
                current_date = match_date
            
            self.set_cached(cache_key, timeline)
            return timeline
    
    def get_standings_at_date(
        self,
        matches: List,
        league: str,
        target_date: date,
    ) -> List[Dict[str, Any]]:
        """
        Get league standings as of specific date.
        
        Args:
            matches: All matches
            league: League code
            target_date: Date to get standings for
            
        Returns:
            List of team standings (sorted by position)
        """
        cache_key = self.generate_cache_key("standings", league, str(target_date))
        cached = self.get_cached(cache_key)
        if cached is not None:
            return cached
        
        with self.track_performance("get_standings_at_date"):
            # Filter matches before target date
            league_matches = [
                m for m in matches
                if self._get_match_field(m, "league") == league
            ]
            
            team_records = defaultdict(lambda: {
                "played": 0, "won": 0, "drawn": 0, "lost": 0,
                "goals_for": 0, "goals_against": 0, "points": 0,
            })
            
            for match in league_matches:
                match_date = self._get_match_field(match, "match_date")
                if not match_date:
                    continue
                
                if isinstance(match_date, str):
                    try:
                        match_date = datetime.fromisoformat(match_date[:10]).date()
                    except (ValueError, TypeError):
                        continue
                
                if match_date < target_date:
                    self._update_records_from_match(team_records, match)
            
            standings = self._records_to_standings(team_records)
            self.set_cached(cache_key, standings)
            return standings
    
    def get_team_position(
        self,
        team: str,
        matches: List,
        league: str,
        target_date: date,
    ) -> Dict[str, Any]:
        """
        Get specific team's position at a date.
        
        Args:
            team: Team name
            matches: All matches
            league: League code
            target_date: Target date
            
        Returns:
            Team position data
        """
        standings = self.get_standings_at_date(matches, league, target_date)
        
        for entry in standings:
            if entry["team"] == team:
                return entry
        
        return {
            "team": team,
            "position": 0,
            "played": 0,
            "points": 0,
            "goal_difference": 0,
        }
    
    def calculate_position_context(
        self,
        team: str,
        matches: List,
        league: str,
        target_date: date,
    ) -> Dict[str, Any]:
        """
        Calculate contextual features for team's position.
        
        Args:
            team: Team name
            matches: All matches
            league: League code
            target_date: Target date
            
        Returns:
            Context features
        """
        standings = self.get_standings_at_date(matches, league, target_date)
        
        if not standings:
            return self._empty_context()
        
        team_entry = None
        team_position = 0
        
        for entry in standings:
            if entry["team"] == team:
                team_entry = entry
                team_position = entry["position"]
                break
        
        if not team_entry:
            return self._empty_context()
        
        # Calculate context
        total_teams = len(standings)
        top_position = self.top_positions
        relegation_start = total_teams - self.relegation_positions + 1
        
        leader_points = standings[0]["points"] if standings else 0
        relegation_points = standings[relegation_start - 1]["points"] if len(standings) >= relegation_start else 0
        
        return {
            "position": team_position,
            "points": team_entry["points"],
            "played": team_entry["played"],
            "goal_difference": team_entry["goal_difference"],
            "points_per_game": round_to_precision(
                team_entry["points"] / max(1, team_entry["played"])
            ),
            "in_title_race": team_position <= 3,
            "in_top_4": team_position <= top_position,
            "in_relegation": team_position >= relegation_start,
            "points_from_top": leader_points - team_entry["points"],
            "points_from_relegation": team_entry["points"] - relegation_points,
            "distance_from_top_4": max(0, team_position - top_position),
            "distance_from_relegation": max(0, relegation_start - team_position),
        }
    
    def get_form_table(
        self,
        matches: List,
        league: str,
        target_date: date,
        n_matches: int = 6,
    ) -> List[Dict[str, Any]]:
        """
        Calculate form table (standings based on last N matches).
        
        Args:
            matches: All matches
            league: League code
            target_date: Target date
            n_matches: Number of recent matches per team
            
        Returns:
            Form table standings
        """
        cache_key = self.generate_cache_key(
            "form_table", league, str(target_date), str(n_matches)
        )
        cached = self.get_cached(cache_key)
        if cached is not None:
            return cached
        
        # Get all teams
        teams = set()
        league_matches = [
            m for m in matches
            if self._get_match_field(m, "league") == league
        ]
        
        for match in league_matches:
            teams.add(self._get_match_field(match, "home_team"))
            teams.add(self._get_match_field(match, "away_team"))
        
        teams = {t for t in teams if t}
        
        # Calculate form for each team
        form_records = {}
        
        for team in teams:
            team_matches = self.filter_matches_for_team(
                league_matches, team, target_date
            )
            
            # Sort and take last N
            team_matches.sort(
                key=lambda m: self._get_match_field(m, "match_date") or date.min,
                reverse=True
            )
            recent = team_matches[:n_matches]
            
            record = {
                "played": 0, "won": 0, "drawn": 0, "lost": 0,
                "goals_for": 0, "goals_against": 0, "points": 0,
            }
            
            for match in recent:
                home = self._get_match_field(match, "home_team")
                fthg = self._get_match_field(match, "fthg")
                ftag = self._get_match_field(match, "ftag")
                
                if fthg is None or ftag is None:
                    continue
                
                is_home = home == team
                gf = fthg if is_home else ftag
                ga = ftag if is_home else fthg
                
                record["played"] += 1
                record["goals_for"] += gf
                record["goals_against"] += ga
                
                if gf > ga:
                    record["won"] += 1
                    record["points"] += 3
                elif gf == ga:
                    record["drawn"] += 1
                    record["points"] += 1
                else:
                    record["lost"] += 1
            
            form_records[team] = record
        
        # Convert to standings
        standings = []
        for team, record in form_records.items():
            standings.append({
                "team": team,
                "played": record["played"],
                "won": record["won"],
                "drawn": record["drawn"],
                "lost": record["lost"],
                "goals_for": record["goals_for"],
                "goals_against": record["goals_against"],
                "goal_difference": record["goals_for"] - record["goals_against"],
                "points": record["points"],
                "points_per_game": round_to_precision(
                    record["points"] / max(1, record["played"])
                ),
            })
        
        # Sort by points, then GD, then GF
        standings.sort(
            key=lambda x: (x["points"], x["goal_difference"], x["goals_for"]),
            reverse=True
        )
        
        # Add positions
        for i, entry in enumerate(standings):
            entry["position"] = i + 1
        
        self.set_cached(cache_key, standings)
        return standings
    
    def _update_records_from_match(
        self,
        records: Dict[str, Dict],
        match: Any,
    ) -> None:
        """Update team records from a match result."""
        home = self._get_match_field(match, "home_team")
        away = self._get_match_field(match, "away_team")
        fthg = self._get_match_field(match, "fthg")
        ftag = self._get_match_field(match, "ftag")
        
        if not home or not away or fthg is None or ftag is None:
            return
        
        # Update home team
        records[home]["played"] += 1
        records[home]["goals_for"] += fthg
        records[home]["goals_against"] += ftag
        
        # Update away team
        records[away]["played"] += 1
        records[away]["goals_for"] += ftag
        records[away]["goals_against"] += fthg
        
        # Determine result
        if fthg > ftag:
            records[home]["won"] += 1
            records[home]["points"] += 3
            records[away]["lost"] += 1
        elif fthg < ftag:
            records[away]["won"] += 1
            records[away]["points"] += 3
            records[home]["lost"] += 1
        else:
            records[home]["drawn"] += 1
            records[home]["points"] += 1
            records[away]["drawn"] += 1
            records[away]["points"] += 1
    
    def _records_to_standings(
        self,
        records: Dict[str, Dict],
    ) -> List[Dict[str, Any]]:
        """Convert team records to sorted standings list."""
        standings = []
        
        for team, record in records.items():
            standings.append({
                "team": team,
                "played": record["played"],
                "won": record["won"],
                "drawn": record["drawn"],
                "lost": record["lost"],
                "goals_for": record["goals_for"],
                "goals_against": record["goals_against"],
                "goal_difference": record["goals_for"] - record["goals_against"],
                "points": record["points"],
                "points_per_game": round_to_precision(
                    record["points"] / max(1, record["played"])
                ),
            })
        
        # Sort by points, then GD, then GF
        standings.sort(
            key=lambda x: (x["points"], x["goal_difference"], x["goals_for"]),
            reverse=True
        )
        
        # Add positions
        for i, entry in enumerate(standings):
            entry["position"] = i + 1
        
        return standings
    
    def _get_match_field(self, match: Any, field: str) -> Any:
        """Get field from match (dict or object)."""
        if isinstance(match, dict):
            return match.get(field)
        return getattr(match, field, None)
    
    def _empty_context(self) -> Dict[str, Any]:
        """Return empty context structure."""
        return {
            "position": 0,
            "points": 0,
            "played": 0,
            "goal_difference": 0,
            "points_per_game": 0.0,
            "in_title_race": False,
            "in_top_4": False,
            "in_relegation": False,
            "points_from_top": 0,
            "points_from_relegation": 0,
            "distance_from_top_4": 0,
            "distance_from_relegation": 0,
        }
