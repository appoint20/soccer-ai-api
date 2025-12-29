"""
Head-to-Head service for analyzing historical matchups between teams.

Calculates H2H statistics, weighted probabilities, and venue-specific
performance for team matchups.
"""
from datetime import date, datetime
from typing import Optional, List, Dict, Any
from collections import defaultdict

from src.domain.services.base_service import BaseService
from src.data.cache.cache_manager import CacheManager
from src.utils.stats_utils import (
    calculate_exponential_weights,
    weighted_average,
    calculate_rate,
    round_to_precision,
)


class H2HService(BaseService):
    """
    Extract and analyze head-to-head statistics between teams.
    
    Features:
    - Overall H2H record
    - Goal statistics from H2H matches
    - Weighted recent form
    - Venue-specific analysis
    """
    
    def __init__(
        self,
        cache_manager: Optional[CacheManager] = None,
        max_meetings: int = 8,
        weight_decay: float = 0.8,
    ):
        """
        Initialize H2H service.
        
        Args:
            cache_manager: Optional cache manager
            max_meetings: Maximum historical meetings to consider
            weight_decay: Decay factor for exponential weighting
        """
        super().__init__(cache_manager)
        self.max_meetings = max_meetings
        self.weight_decay = weight_decay
    
    def get_h2h_stats(
        self,
        home_team: str,
        away_team: str,
        matches: List,
        max_meetings: Optional[int] = None,
    ) -> Dict[str, Any]:
        """
        Get H2H statistics between two teams.
        
        Args:
            home_team: Home team name
            away_team: Away team name  
            matches: All historical matches
            max_meetings: Override default max meetings
            
        Returns:
            Dict with H2H statistics
        """
        home_team = self.validate_team_name(home_team)
        away_team = self.validate_team_name(away_team)
        
        if not home_team or not away_team:
            return self._empty_h2h_stats()
        
        # Check cache
        cache_key = self.generate_cache_key("h2h", home_team, away_team)
        cached = self.get_cached(cache_key)
        if cached is not None:
            return cached
        
        with self.track_performance("get_h2h_stats"):
            # Extract H2H matches
            h2h_matches = self.extract_h2h_matches(
                home_team, away_team, matches,
                max_meetings or self.max_meetings
            )
            
            if not h2h_matches:
                return self._empty_h2h_stats()
            
            stats = {
                "home_team": home_team,
                "away_team": away_team,
                "total_meetings": len(h2h_matches),
                "date_range": self._get_date_range(h2h_matches),
                "overall_record": self._calculate_overall_record(
                    h2h_matches, home_team, away_team
                ),
                "goal_statistics": self._calculate_goal_stats(h2h_matches),
                "recent_meetings": self._format_recent_meetings(
                    h2h_matches, home_team
                ),
                "weighted_stats": self._calculate_weighted_stats(
                    h2h_matches, home_team
                ),
                "venue_split": self._calculate_venue_split(
                    h2h_matches, home_team, away_team
                ),
                "last_updated": datetime.now().isoformat()[:10],
            }
            
            self.set_cached(cache_key, stats)
            return stats
    
    def extract_h2h_matches(
        self,
        team_a: str,
        team_b: str,
        matches: List,
        max_meetings: Optional[int] = None,
    ) -> List:
        """
        Extract all matches between two teams.
        
        Args:
            team_a: First team
            team_b: Second team
            matches: All matches
            max_meetings: Maximum matches to return
            
        Returns:
            List of H2H matches, sorted by date (most recent first)
        """
        h2h = []
        
        for match in matches:
            if isinstance(match, dict):
                home = match.get("home_team")
                away = match.get("away_team")
                match_date = match.get("match_date")
            else:
                home = getattr(match, "home_team", None)
                away = getattr(match, "away_team", None)
                match_date = getattr(match, "match_date", None)
            
            # Check if this is an H2H match (either direction)
            is_h2h = (
                (home == team_a and away == team_b) or
                (home == team_b and away == team_a)
            )
            
            if is_h2h:
                h2h.append(match)
        
        # Sort by date (most recent first)
        h2h.sort(
            key=lambda m: (
                m.get("match_date") if isinstance(m, dict)
                else getattr(m, "match_date", date.min)
            ),
            reverse=True
        )
        
        # Limit to max meetings
        if max_meetings:
            h2h = h2h[:max_meetings]
        
        return h2h
    
    def get_h2h_trend(
        self,
        home_team: str,
        away_team: str,
        matches: List,
    ) -> str:
        """
        Analyze if one team is improving/declining against the other.
        
        Args:
            home_team: Home team 
            away_team: Away team
            matches: All matches
            
        Returns:
            'home_improving', 'away_improving', or 'stable'
        """
        h2h_matches = self.extract_h2h_matches(home_team, away_team, matches, 10)
        
        if len(h2h_matches) < 4:
            return "stable"
        
        # Compare first half vs second half
        mid = len(h2h_matches) // 2
        recent = h2h_matches[:mid]
        older = h2h_matches[mid:]
        
        recent_home_goals = 0
        recent_away_goals = 0
        older_home_goals = 0
        older_away_goals = 0
        
        for match in recent:
            goals = self._extract_h2h_goals(match, home_team)
            if goals:
                recent_home_goals += goals["home_goals"]
                recent_away_goals += goals["away_goals"]
        
        for match in older:
            goals = self._extract_h2h_goals(match, home_team)
            if goals:
                older_home_goals += goals["home_goals"]
                older_away_goals += goals["away_goals"]
        
        recent_diff = (recent_home_goals - recent_away_goals) / max(1, len(recent))
        older_diff = (older_home_goals - older_away_goals) / max(1, len(older))
        
        if recent_diff > older_diff + 0.5:
            return "home_improving"
        elif recent_diff < older_diff - 0.5:
            return "away_improving"
        else:
            return "stable"
    
    def _calculate_overall_record(
        self,
        h2h_matches: List,
        home_team: str,
        away_team: str,
    ) -> Dict[str, Any]:
        """Calculate overall H2H record."""
        home_wins = 0
        away_wins = 0
        draws = 0
        home_goals = 0
        away_goals = 0
        
        for match in h2h_matches:
            goals = self._extract_h2h_goals(match, home_team)
            if goals is None:
                continue
            
            home_goals += goals["home_goals"]
            away_goals += goals["away_goals"]
            
            if goals["home_goals"] > goals["away_goals"]:
                home_wins += 1
            elif goals["home_goals"] < goals["away_goals"]:
                away_wins += 1
            else:
                draws += 1
        
        n = len(h2h_matches)
        
        return {
            "home_wins": home_wins,
            "away_wins": away_wins,
            "draws": draws,
            "home_goals": home_goals,
            "away_goals": away_goals,
            "home_goals_avg": round_to_precision(home_goals / n) if n > 0 else 0.0,
            "away_goals_avg": round_to_precision(away_goals / n) if n > 0 else 0.0,
        }
    
    def _calculate_goal_stats(self, h2h_matches: List) -> Dict[str, Any]:
        """Calculate goal-related H2H statistics."""
        total_goals = []
        over25_count = 0
        btts_count = 0
        scorelines = defaultdict(int)
        
        for match in h2h_matches:
            if isinstance(match, dict):
                fthg = match.get("fthg")
                ftag = match.get("ftag")
            else:
                fthg = getattr(match, "fthg", None)
                ftag = getattr(match, "ftag", None)
            
            if fthg is None or ftag is None:
                continue
            
            total = fthg + ftag
            total_goals.append(total)
            
            if total > 2.5:
                over25_count += 1
            
            if fthg > 0 and ftag > 0:
                btts_count += 1
            
            scoreline = f"{fthg}-{ftag}"
            scorelines[scoreline] += 1
        
        n = len(total_goals)
        if n == 0:
            return {
                "avg_total_goals": 0.0,
                "over25_rate": 0.0,
                "btts_rate": 0.0,
                "highest_scoring": "0-0",
                "most_common_scoreline": "0-0",
            }
        
        # Find most common scoreline
        most_common = max(scorelines.items(), key=lambda x: x[1], default=("0-0", 0))
        
        # Find highest scoring
        highest_idx = total_goals.index(max(total_goals)) if total_goals else 0
        if isinstance(h2h_matches[highest_idx], dict):
            hg = h2h_matches[highest_idx].get("fthg", 0)
            ag = h2h_matches[highest_idx].get("ftag", 0)
        else:
            hg = getattr(h2h_matches[highest_idx], "fthg", 0)
            ag = getattr(h2h_matches[highest_idx], "ftag", 0)
        
        return {
            "avg_total_goals": round_to_precision(sum(total_goals) / n),
            "over25_rate": round_to_precision(calculate_rate(over25_count, n)),
            "btts_rate": round_to_precision(calculate_rate(btts_count, n)),
            "highest_scoring": f"{hg}-{ag}",
            "most_common_scoreline": most_common[0],
        }
    
    def _calculate_weighted_stats(
        self,
        h2h_matches: List,
        home_team: str,
    ) -> Dict[str, Any]:
        """Calculate exponentially weighted H2H statistics."""
        n = len(h2h_matches)
        if n == 0:
            return {
                "over25_probability": 0.5,
                "btts_probability": 0.5,
                "home_win_probability": 0.33,
                "draw_probability": 0.34,
                "away_win_probability": 0.33,
            }
        
        weights = calculate_exponential_weights(n, self.weight_decay)
        
        over25_values = []
        btts_values = []
        home_win_values = []
        draw_values = []
        away_win_values = []
        
        for match in h2h_matches:
            goals = self._extract_h2h_goals(match, home_team)
            if goals is None:
                continue
            
            total = goals["home_goals"] + goals["away_goals"]
            
            over25_values.append(1.0 if total > 2.5 else 0.0)
            btts_values.append(1.0 if goals["home_goals"] > 0 and goals["away_goals"] > 0 else 0.0)
            
            if goals["home_goals"] > goals["away_goals"]:
                home_win_values.append(1.0)
                draw_values.append(0.0)
                away_win_values.append(0.0)
            elif goals["home_goals"] < goals["away_goals"]:
                home_win_values.append(0.0)
                draw_values.append(0.0)
                away_win_values.append(1.0)
            else:
                home_win_values.append(0.0)
                draw_values.append(1.0)
                away_win_values.append(0.0)
        
        return {
            "over25_probability": round_to_precision(weighted_average(over25_values, weights)),
            "btts_probability": round_to_precision(weighted_average(btts_values, weights)),
            "home_win_probability": round_to_precision(weighted_average(home_win_values, weights)),
            "draw_probability": round_to_precision(weighted_average(draw_values, weights)),
            "away_win_probability": round_to_precision(weighted_average(away_win_values, weights)),
        }
    
    def _calculate_venue_split(
        self,
        h2h_matches: List,
        home_team: str,
        away_team: str,
    ) -> Dict[str, Dict[str, Any]]:
        """Calculate venue-specific H2H stats."""
        at_home = []
        at_away = []
        
        for match in h2h_matches:
            if isinstance(match, dict):
                home = match.get("home_team")
            else:
                home = getattr(match, "home_team", None)
            
            if home == home_team:
                at_home.append(match)
            else:
                at_away.append(match)
        
        return {
            "at_home": self._venue_stats(at_home, home_team, True),
            "at_away": self._venue_stats(at_away, away_team, False),
        }
    
    def _venue_stats(
        self,
        matches: List,
        perspective_team: str,
        is_home: bool,
    ) -> Dict[str, Any]:
        """Calculate stats from perspective of one team."""
        n = len(matches)
        if n == 0:
            return {"meetings": 0, "home_wins": 0, "home_goals_avg": 0.0, "away_goals_avg": 0.0}
        
        wins = 0
        goals_for = 0
        goals_against = 0
        
        for match in matches:
            if isinstance(match, dict):
                fthg = match.get("fthg", 0)
                ftag = match.get("ftag", 0)
            else:
                fthg = getattr(match, "fthg", 0) or 0
                ftag = getattr(match, "ftag", 0) or 0
            
            if is_home:
                goals_for += fthg
                goals_against += ftag
                if fthg > ftag:
                    wins += 1
            else:
                goals_for += ftag
                goals_against += fthg
                if ftag > fthg:
                    wins += 1
        
        return {
            "meetings": n,
            "home_wins": wins,
            "home_goals_avg": round_to_precision(goals_for / n),
            "away_goals_avg": round_to_precision(goals_against / n),
        }
    
    def _format_recent_meetings(
        self,
        h2h_matches: List,
        home_team: str,
    ) -> List[Dict[str, Any]]:
        """Format recent meetings for output."""
        weights = calculate_exponential_weights(len(h2h_matches), self.weight_decay)
        result = []
        
        for i, match in enumerate(h2h_matches):
            if isinstance(match, dict):
                match_date = match.get("match_date")
                home = match.get("home_team")
                fthg = match.get("fthg", 0)
                ftag = match.get("ftag", 0)
            else:
                match_date = getattr(match, "match_date", None)
                home = getattr(match, "home_team", None)
                fthg = getattr(match, "fthg", 0) or 0
                ftag = getattr(match, "ftag", 0) or 0
            
            # Determine result from perspective of home_team parameter
            if home == home_team:
                if fthg > ftag:
                    result_code = "H"
                elif fthg < ftag:
                    result_code = "A"
                else:
                    result_code = "D"
            else:
                if ftag > fthg:
                    result_code = "H"
                elif ftag < fthg:
                    result_code = "A"
                else:
                    result_code = "D"
            
            result.append({
                "date": str(match_date)[:10] if match_date else "Unknown",
                "venue": home,
                "score": f"{fthg}-{ftag}",
                "result": result_code,
                "weight": round_to_precision(weights[i] if i < len(weights) else 0),
            })
        
        return result
    
    def _extract_h2h_goals(
        self,
        match: Any,
        home_team: str,
    ) -> Optional[Dict[str, int]]:
        """Extract goals from H2H match perspective."""
        if isinstance(match, dict):
            home = match.get("home_team")
            fthg = match.get("fthg")
            ftag = match.get("ftag")
        else:
            home = getattr(match, "home_team", None)
            fthg = getattr(match, "fthg", None)
            ftag = getattr(match, "ftag", None)
        
        if fthg is None or ftag is None:
            return None
        
        # Return from perspective of home_team parameter
        if home == home_team:
            return {"home_goals": fthg, "away_goals": ftag}
        else:
            return {"home_goals": ftag, "away_goals": fthg}
    
    def _get_date_range(self, h2h_matches: List) -> Dict[str, str]:
        """Get date range of H2H matches."""
        dates = []
        for match in h2h_matches:
            if isinstance(match, dict):
                match_date = match.get("match_date")
            else:
                match_date = getattr(match, "match_date", None)
            if match_date:
                dates.append(str(match_date)[:10])
        
        if not dates:
            return {"first": "Unknown", "last": "Unknown"}
        
        return {"first": min(dates), "last": max(dates)}
    
    def _empty_h2h_stats(self) -> Dict[str, Any]:
        """Return empty H2H stats structure."""
        return {
            "home_team": "",
            "away_team": "",
            "total_meetings": 0,
            "date_range": {"first": "Unknown", "last": "Unknown"},
            "overall_record": {
                "home_wins": 0,
                "away_wins": 0,
                "draws": 0,
                "home_goals": 0,
                "away_goals": 0,
                "home_goals_avg": 0.0,
                "away_goals_avg": 0.0,
            },
            "goal_statistics": {
                "avg_total_goals": 0.0,
                "over25_rate": 0.0,
                "btts_rate": 0.0,
            },
            "recent_meetings": [],
            "weighted_stats": {
                "over25_probability": 0.5,
                "btts_probability": 0.5,
                "home_win_probability": 0.33,
                "draw_probability": 0.34,
                "away_win_probability": 0.33,
            },
            "venue_split": {},
            "last_updated": datetime.now().isoformat()[:10],
        }
