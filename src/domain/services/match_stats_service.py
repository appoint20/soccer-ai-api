"""
Match statistics service for BTTS and Over 2.5 calculations.

Calculates percentages based on:
- Last 9 overall matches (home + away)
- Last 6 home-only matches for home team
- Last 6 away-only matches for away team
"""
from typing import Dict, List, Any, Optional
from datetime import date, datetime

from src.domain.services.base_service import BaseService
from src.data.cache.cache_manager import CacheManager


class MatchStatsService(BaseService):
    """
    Calculate BTTS and Over 2.5 statistics for upcoming matches.
    
    Uses:
    - Last 9 matches overall + Last 6 venue-specific
    """
    
    def __init__(self, cache_manager: Optional[CacheManager] = None):
        super().__init__(cache_manager)
    
    def calculate_match_stats(
        self,
        home_team: str,
        away_team: str,
        matches: List[Dict],
        as_of_date: Optional[date] = None,
        league: Optional[str] = None,
    ) -> Dict[str, Any]:
        """
        Calculate BTTS and Over 2.5 percentages.
        
        Args:
            home_team: Home team name
            away_team: Away team name
            matches: All historical matches
            as_of_date: Calculate stats as of this date
            league: Optional league filter
            
        Returns:
            Dict with BTTS and Over25 percentages
        """
        # Get team matches
        home_matches = self._get_team_matches(home_team, matches, as_of_date, league)
        away_matches = self._get_team_matches(away_team, matches, as_of_date, league)
        
        # Split by venue
        home_team_home = [m for m in home_matches if m.get("home_team") == home_team]
        home_team_away = [m for m in home_matches if m.get("away_team") == home_team]
        
        away_team_home = [m for m in away_matches if m.get("home_team") == away_team]
        away_team_away = [m for m in away_matches if m.get("away_team") == away_team]
        
        # Home team stats
        home_last_9 = home_matches[:9]  # Last 9 overall
        home_last_6_home = home_team_home[:6]  # Last 6 at home
        
        # Away team stats  
        away_last_9 = away_matches[:9]  # Last 9 overall
        away_last_6_away = away_team_away[:6]  # Last 6 away
        
        # Calculate BTTS percentages
        home_btts_overall = self._btts_rate(home_last_9)
        home_btts_home = self._btts_rate(home_last_6_home)
        
        away_btts_overall = self._btts_rate(away_last_9)
        away_btts_away = self._btts_rate(away_last_6_away)
        
        # Calculate Over 2.5 percentages
        home_o25_overall = self._over25_rate(home_last_9)
        home_o25_home = self._over25_rate(home_last_6_home)
        
        away_o25_overall = self._over25_rate(away_last_9)
        away_o25_away = self._over25_rate(away_last_6_away)
        
        # Combined percentages (weighted average)
        combined_btts = self._calculate_combined(
            home_btts_overall, home_btts_home,
            away_btts_overall, away_btts_away
        )
        
        combined_o25 = self._calculate_combined(
            home_o25_overall, home_o25_home,
            away_o25_overall, away_o25_away
        )
        
        # Calculate home team scored in last 9
        home_scored_overall = self._scored_rate(home_last_9, home_team)
        home_scored_home = self._scored_rate(home_last_6_home, home_team)
        
        # Calculate away team scored in last 9
        away_scored_overall = self._scored_rate(away_last_9, away_team)
        away_scored_away = self._scored_rate(away_last_6_away, away_team)
        
        return {
            "btts": {
                "combined_pct": round(combined_btts * 100, 1),
                "home_team": {
                    "overall_9": {
                        "matches": len(home_last_9),
                        "btts_count": sum(1 for m in home_last_9 if self._is_btts(m)),
                        "pct": round(home_btts_overall * 100, 1),
                    },
                    "home_6": {
                        "matches": len(home_last_6_home),
                        "btts_count": sum(1 for m in home_last_6_home if self._is_btts(m)),
                        "pct": round(home_btts_home * 100, 1),
                    },
                    "scored_overall_9": {
                        "matches": len(home_last_9),
                        "scored": sum(1 for m in home_last_9 if self._team_scored(m, home_team)),
                        "pct": round(home_scored_overall * 100, 1),
                    },
                    "scored_home_6": {
                        "matches": len(home_last_6_home),
                        "scored": sum(1 for m in home_last_6_home if self._team_scored(m, home_team)),
                        "pct": round(home_scored_home * 100, 1),
                    },
                },
                "away_team": {
                    "overall_9": {
                        "matches": len(away_last_9),
                        "btts_count": sum(1 for m in away_last_9 if self._is_btts(m)),
                        "pct": round(away_btts_overall * 100, 1),
                    },
                    "away_6": {
                        "matches": len(away_last_6_away),
                        "btts_count": sum(1 for m in away_last_6_away if self._is_btts(m)),
                        "pct": round(away_btts_away * 100, 1),
                    },
                    "scored_overall_9": {
                        "matches": len(away_last_9),
                        "scored": sum(1 for m in away_last_9 if self._team_scored(m, away_team)),
                        "pct": round(away_scored_overall * 100, 1),
                    },
                    "scored_away_6": {
                        "matches": len(away_last_6_away),
                        "scored": sum(1 for m in away_last_6_away if self._team_scored(m, away_team)),
                        "pct": round(away_scored_away * 100, 1),
                    },
                },
            },
            "over25": {
                "combined_pct": round(combined_o25 * 100, 1),
                "home_team": {
                    "overall_9": {
                        "matches": len(home_last_9),
                        "over25_count": sum(1 for m in home_last_9 if self._is_over25(m)),
                        "pct": round(home_o25_overall * 100, 1),
                    },
                    "home_6": {
                        "matches": len(home_last_6_home),
                        "over25_count": sum(1 for m in home_last_6_home if self._is_over25(m)),
                        "pct": round(home_o25_home * 100, 1),
                    },
                },
                "away_team": {
                    "overall_9": {
                        "matches": len(away_last_9),
                        "over25_count": sum(1 for m in away_last_9 if self._is_over25(m)),
                        "pct": round(away_o25_overall * 100, 1),
                    },
                    "away_6": {
                        "matches": len(away_last_6_away),
                        "over25_count": sum(1 for m in away_last_6_away if self._is_over25(m)),
                        "pct": round(away_o25_away * 100, 1),
                    },
                },
            },
        }
    
    def _get_team_matches(
        self,
        team: str,
        matches: List[Dict],
        as_of_date: Optional[date],
        league: Optional[str],
    ) -> List[Dict]:
        """Get team's matches sorted by date (most recent first)."""
        team_matches = []
        
        for m in matches:
            if m.get("home_team") != team and m.get("away_team") != team:
                continue
            
            # Check date
            match_date = m.get("match_date")
            if match_date:
                if isinstance(match_date, str):
                    match_date = datetime.fromisoformat(match_date[:10]).date()
                elif isinstance(match_date, datetime):
                    match_date = match_date.date()
                
                if as_of_date and match_date >= as_of_date:
                    continue
            
            # Check league (optional)
            if league and m.get("league") != league:
                continue
            
            # Check result exists
            if m.get("fthg") is None or m.get("ftag") is None:
                continue
            
            team_matches.append(m)
        
        # Sort by date (most recent first)
        team_matches.sort(
            key=lambda x: x.get("match_date", ""),
            reverse=True
        )
        
        return team_matches
    
    def _is_btts(self, match: Dict) -> bool:
        """Check if match had both teams scoring."""
        fthg = match.get("fthg", 0)
        ftag = match.get("ftag", 0)
        return fthg > 0 and ftag > 0
    
    def _is_over25(self, match: Dict) -> bool:
        """Check if match had over 2.5 goals."""
        fthg = match.get("fthg", 0)
        ftag = match.get("ftag", 0)
        return (fthg + ftag) > 2.5
    
    def _team_scored(self, match: Dict, team: str) -> bool:
        """Check if team scored in match."""
        if match.get("home_team") == team:
            return match.get("fthg", 0) > 0
        else:
            return match.get("ftag", 0) > 0
    
    def _btts_rate(self, matches: List[Dict]) -> float:
        """Calculate BTTS rate."""
        if not matches:
            return 0.0
        btts_count = sum(1 for m in matches if self._is_btts(m))
        return btts_count / len(matches)
    
    def _over25_rate(self, matches: List[Dict]) -> float:
        """Calculate Over 2.5 rate."""
        if not matches:
            return 0.0
        o25_count = sum(1 for m in matches if self._is_over25(m))
        return o25_count / len(matches)
    
    def _scored_rate(self, matches: List[Dict], team: str) -> float:
        """Calculate rate of team scoring."""
        if not matches:
            return 0.0
        scored_count = sum(1 for m in matches if self._team_scored(m, team))
        return scored_count / len(matches)
    
    def _calculate_combined(
        self,
        team1_overall: float,
        team1_venue: float,
        team2_overall: float,
        team2_venue: float,
    ) -> float:
        """
        Calculate combined percentage.
        
        Weights:
        - Overall (9 matches): 40%
        - Venue-specific (6 matches): 60%
        """
        team1_avg = team1_overall * 0.4 + team1_venue * 0.6
        team2_avg = team2_overall * 0.4 + team2_venue * 0.6
        
        # Average of both teams
        return (team1_avg + team2_avg) / 2
