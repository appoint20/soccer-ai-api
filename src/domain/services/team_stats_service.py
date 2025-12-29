"""
Team statistics service for calculating comprehensive team metrics.

Calculates overall stats, home/away splits, recent form, and
scoring patterns used as features for ML models.
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
    calculate_form_points,
    results_to_form_string,
    detect_trend,
    round_to_precision,
)
from src.utils.date_utils import (
    get_season_from_date,
    get_month_name,
    filter_matches_before_date,
)


class TeamStatsService(BaseService):
    """
    Calculate comprehensive team statistics.
    
    Features calculated:
    - Overall season stats
    - Home/away splits  
    - Recent form (last 5, last 10)
    - Scoring patterns
    - Monthly breakdown
    """
    
    def __init__(
        self,
        cache_manager: Optional[CacheManager] = None,
        min_matches: int = 3,
    ):
        """
        Initialize team stats service.
        
        Args:
            cache_manager: Optional cache manager
            min_matches: Minimum matches required for reliable stats
        """
        super().__init__(cache_manager)
        self.min_matches = min_matches
    
    def calculate_team_stats(
        self,
        team_name: str,
        matches: List,
        as_of_date: Optional[date] = None,
        league: Optional[str] = None,
    ) -> Dict[str, Any]:
        """
        Calculate all statistics for a team.
        
        Args:
            team_name: Name of team
            matches: List of Match objects or dicts
            as_of_date: Calculate stats only up to this date (time-travel)
            league: Optional league filter
            
        Returns:
            Dict with all team statistics
        """
        team_name = self.validate_team_name(team_name)
        if not team_name:
            return self._empty_stats()
        
        # Check cache
        cache_key = self.generate_cache_key(
            "team_stats", team_name, 
            str(as_of_date or "all"),
            league or "all"
        )
        cached = self.get_cached(cache_key)
        if cached is not None:
            return cached
        
        with self.track_performance("calculate_team_stats"):
            # Filter matches for team
            team_matches = self.filter_matches_for_team(
                matches, team_name, as_of_date
            )
            
            if not team_matches:
                return self._empty_stats()
            
            # Sort by date (oldest first for chronological form)
            team_matches = sorted(
                team_matches,
                key=lambda m: (
                    m.get("match_date") if isinstance(m, dict) 
                    else getattr(m, "match_date", date.min)
                ),
                reverse=True  # Most recent first
            )
            
            # Calculate all stat categories
            stats = {
                "team": team_name,
                "league": self._get_team_league(team_matches),
                "season": self._get_team_season(team_matches),
                "last_updated": datetime.now().isoformat()[:10],
                "overall": self._calculate_overall_stats(team_matches, team_name),
                "home": self._calculate_venue_stats(team_matches, team_name, is_home=True),
                "away": self._calculate_venue_stats(team_matches, team_name, is_home=False),
                "form_last_5": self._calculate_form_stats(team_matches, team_name, n=5),
                "form_last_10": self._calculate_form_stats(team_matches, team_name, n=10),
                "scoring_patterns": self._calculate_scoring_patterns(team_matches, team_name),
                "by_month": self._calculate_monthly_stats(team_matches, team_name),
            }
            
            self.set_cached(cache_key, stats)
            return stats
    
    def get_team_form(
        self,
        team_name: str,
        matches: List,
        n_matches: int = 5,
        as_of_date: Optional[date] = None,
    ) -> Dict[str, Any]:
        """
        Get last N matches form for a team.
        
        Args:
            team_name: Team name
            matches: All matches
            n_matches: Number of recent matches
            as_of_date: Time-travel date
            
        Returns:
            Form statistics dict
        """
        team_matches = self.filter_matches_for_team(
            matches, team_name, as_of_date
        )
        
        if not team_matches:
            return {"form_string": "", "points": 0, "matches": 0}
        
        # Sort most recent first
        team_matches = sorted(
            team_matches,
            key=lambda m: (
                m.get("match_date") if isinstance(m, dict)
                else getattr(m, "match_date", date.min)
            ),
            reverse=True
        )[:n_matches]
        
        return self._calculate_form_stats(team_matches, team_name, n=n_matches)
    
    def calculate_all_teams_stats(
        self,
        matches: List,
        as_of_date: Optional[date] = None,
    ) -> Dict[str, Dict[str, Any]]:
        """
        Calculate stats for all teams in dataset.
        
        Args:
            matches: All matches
            as_of_date: Time-travel date
            
        Returns:
            Dict mapping team name to stats
        """
        # Get all unique team names
        teams = set()
        for match in matches:
            if isinstance(match, dict):
                teams.add(match.get("home_team"))
                teams.add(match.get("away_team"))
            else:
                teams.add(getattr(match, "home_team", None))
                teams.add(getattr(match, "away_team", None))
        
        teams = {t for t in teams if t}
        
        self.logger.info(f"Calculating stats for {len(teams)} teams")
        
        all_stats = {}
        for team in teams:
            all_stats[team] = self.calculate_team_stats(
                team, matches, as_of_date
            )
        
        return all_stats
    
    def _calculate_overall_stats(
        self,
        matches: List,
        team_name: str,
    ) -> Dict[str, Any]:
        """Calculate overall season statistics."""
        if not matches:
            return self._empty_venue_stats()
        
        stats = defaultdict(int)
        goals_scored = []
        goals_conceded = []
        
        for match in matches:
            match_stats = self._extract_match_stats(match, team_name)
            if match_stats is None:
                continue
            
            stats["matches"] += 1
            stats["goals_scored"] += match_stats["goals_for"]
            stats["goals_conceded"] += match_stats["goals_against"]
            stats["wins"] += 1 if match_stats["result"] == "W" else 0
            stats["draws"] += 1 if match_stats["result"] == "D" else 0
            stats["losses"] += 1 if match_stats["result"] == "L" else 0
            
            total_goals = match_stats["goals_for"] + match_stats["goals_against"]
            stats["over25"] += 1 if total_goals > 2.5 else 0
            stats["btts"] += 1 if match_stats["goals_for"] > 0 and match_stats["goals_against"] > 0 else 0
            stats["clean_sheets"] += 1 if match_stats["goals_against"] == 0 else 0
            stats["failed_to_score"] += 1 if match_stats["goals_for"] == 0 else 0
            
            goals_scored.append(match_stats["goals_for"])
            goals_conceded.append(match_stats["goals_against"])
        
        n = stats["matches"]
        if n == 0:
            return self._empty_venue_stats()
        
        return {
            "matches": n,
            "wins": stats["wins"],
            "draws": stats["draws"],
            "losses": stats["losses"],
            "goals_scored": stats["goals_scored"],
            "goals_conceded": stats["goals_conceded"],
            "goals_scored_avg": round_to_precision(stats["goals_scored"] / n),
            "goals_conceded_avg": round_to_precision(stats["goals_conceded"] / n),
            "win_rate": round_to_precision(calculate_rate(stats["wins"], n)),
            "draw_rate": round_to_precision(calculate_rate(stats["draws"], n)),
            "loss_rate": round_to_precision(calculate_rate(stats["losses"], n)),
            "over25_rate": round_to_precision(calculate_rate(stats["over25"], n)),
            "btts_rate": round_to_precision(calculate_rate(stats["btts"], n)),
            "clean_sheet_rate": round_to_precision(calculate_rate(stats["clean_sheets"], n)),
            "failed_to_score_rate": round_to_precision(calculate_rate(stats["failed_to_score"], n)),
            "points": stats["wins"] * 3 + stats["draws"],
            "points_per_game": round_to_precision((stats["wins"] * 3 + stats["draws"]) / n),
        }
    
    def _calculate_venue_stats(
        self,
        matches: List,
        team_name: str,
        is_home: bool,
    ) -> Dict[str, Any]:
        """Calculate home or away statistics."""
        venue_matches = [
            m for m in matches
            if self._is_team_at_venue(m, team_name, is_home)
        ]
        
        return self._calculate_overall_stats(venue_matches, team_name)
    
    def _calculate_form_stats(
        self,
        matches: List,
        team_name: str,
        n: int = 5,
    ) -> Dict[str, Any]:
        """Calculate recent form statistics."""
        recent = matches[:n]
        
        if not recent:
            return {
                "results": [],
                "form_string": "",
                "points": 0,
                "matches": 0,
            }
        
        results = []
        goals_scored = []
        goals_conceded = []
        
        for match in recent:
            match_stats = self._extract_match_stats(match, team_name)
            if match_stats:
                results.append(match_stats["result"])
                goals_scored.append(match_stats["goals_for"])
                goals_conceded.append(match_stats["goals_against"])
        
        n_matches = len(results)
        if n_matches == 0:
            return {
                "results": [],
                "form_string": "",
                "points": 0,
                "matches": 0,
            }
        
        over25_count = sum(
            1 for i in range(n_matches)
            if goals_scored[i] + goals_conceded[i] > 2.5
        )
        btts_count = sum(
            1 for i in range(n_matches)
            if goals_scored[i] > 0 and goals_conceded[i] > 0
        )
        
        return {
            "results": results,
            "form_string": results_to_form_string(results, n),
            "goals_scored": goals_scored,
            "goals_conceded": goals_conceded,
            "goals_scored_avg": round_to_precision(sum(goals_scored) / n_matches),
            "goals_conceded_avg": round_to_precision(sum(goals_conceded) / n_matches),
            "over25_rate": round_to_precision(calculate_rate(over25_count, n_matches)),
            "btts_rate": round_to_precision(calculate_rate(btts_count, n_matches)),
            "points": calculate_form_points(results),
            "matches": n_matches,
            "trend": detect_trend(goals_scored),
        }
    
    def _calculate_scoring_patterns(
        self,
        matches: List,
        team_name: str,
    ) -> Dict[str, Any]:
        """Calculate scoring patterns (first half vs second half, etc.)."""
        first_half_goals = []
        second_half_goals = []
        scored_first_count = 0
        leading_at_ht = 0
        n_valid = 0
        
        for match in matches:
            match_stats = self._extract_match_stats(match, team_name)
            if match_stats is None:
                continue
            
            ht_for = match_stats.get("ht_goals_for")
            ht_against = match_stats.get("ht_goals_against")
            
            if ht_for is not None and ht_against is not None:
                first_half_goals.append(ht_for)
                second_half = match_stats["goals_for"] - ht_for
                second_half_goals.append(max(0, second_half))
                
                if ht_for > ht_against:
                    leading_at_ht += 1
                
                n_valid += 1
        
        if n_valid == 0:
            return {
                "first_half_goals_avg": 0.0,
                "second_half_goals_avg": 0.0,
                "scored_first_rate": 0.0,
                "leading_at_ht_rate": 0.0,
            }
        
        return {
            "first_half_goals_avg": round_to_precision(sum(first_half_goals) / n_valid),
            "second_half_goals_avg": round_to_precision(sum(second_half_goals) / n_valid),
            "scored_first_rate": 0.0,  # Would need more data
            "leading_at_ht_rate": round_to_precision(calculate_rate(leading_at_ht, n_valid)),
        }
    
    def _calculate_monthly_stats(
        self,
        matches: List,
        team_name: str,
    ) -> Dict[str, Dict[str, Any]]:
        """Calculate statistics by month."""
        by_month = defaultdict(list)
        
        for match in matches:
            if isinstance(match, dict):
                match_date = match.get("match_date")
            else:
                match_date = getattr(match, "match_date", None)
            
            if match_date:
                if isinstance(match_date, str):
                    try:
                        match_date = datetime.fromisoformat(match_date[:10]).date()
                    except:
                        continue
                
                month = get_month_name(match_date)
                by_month[month].append(match)
        
        result = {}
        for month, month_matches in by_month.items():
            stats = self._calculate_overall_stats(month_matches, team_name)
            result[month] = {
                "matches": stats["matches"],
                "goals_avg": stats["goals_scored_avg"],
                "over25_rate": stats["over25_rate"],
                "btts_rate": stats["btts_rate"],
            }
        
        return result
    
    def _extract_match_stats(
        self,
        match: Any,
        team_name: str,
    ) -> Optional[Dict[str, Any]]:
        """Extract stats for a team from a match."""
        if isinstance(match, dict):
            home = match.get("home_team")
            away = match.get("away_team")
            fthg = match.get("fthg")
            ftag = match.get("ftag")
            hthg = match.get("hthg")
            htag = match.get("htag")
        else:
            home = getattr(match, "home_team", None)
            away = getattr(match, "away_team", None)
            fthg = getattr(match, "fthg", None)
            ftag = getattr(match, "ftag", None)
            hthg = getattr(match, "hthg", None)
            htag = getattr(match, "htag", None)
        
        # Can't calculate without goals
        if fthg is None or ftag is None:
            return None
        
        is_home = team_name == home
        is_away = team_name == away
        
        if not is_home and not is_away:
            return None
        
        if is_home:
            goals_for = fthg
            goals_against = ftag
            ht_for = hthg
            ht_against = htag
        else:
            goals_for = ftag
            goals_against = fthg
            ht_for = htag
            ht_against = hthg
        
        # Determine result
        if goals_for > goals_against:
            result = "W"
        elif goals_for < goals_against:
            result = "L"
        else:
            result = "D"
        
        return {
            "goals_for": goals_for,
            "goals_against": goals_against,
            "ht_goals_for": ht_for,
            "ht_goals_against": ht_against,
            "result": result,
            "is_home": is_home,
        }
    
    def _is_team_at_venue(self, match: Any, team_name: str, is_home: bool) -> bool:
        """Check if team is playing at specified venue."""
        if isinstance(match, dict):
            home = match.get("home_team")
            away = match.get("away_team")
        else:
            home = getattr(match, "home_team", None)
            away = getattr(match, "away_team", None)
        
        if is_home:
            return team_name == home
        else:
            return team_name == away
    
    def _get_team_league(self, matches: List) -> str:
        """Get league from matches."""
        for match in matches:
            if isinstance(match, dict):
                league = match.get("league")
            else:
                league = getattr(match, "league", None)
            if league:
                return league
        return "Unknown"
    
    def _get_team_season(self, matches: List) -> str:
        """Get season from matches."""
        for match in matches:
            if isinstance(match, dict):
                season = match.get("season")
            else:
                season = getattr(match, "season", None)
            if season:
                return season
        
        # Try to calculate from match date
        for match in matches:
            if isinstance(match, dict):
                match_date = match.get("match_date")
            else:
                match_date = getattr(match, "match_date", None)
            if match_date:
                return get_season_from_date(match_date)
        
        return "Unknown"
    
    def _empty_stats(self) -> Dict[str, Any]:
        """Return empty stats structure."""
        return {
            "team": "",
            "league": "Unknown",
            "season": "Unknown",
            "last_updated": datetime.now().isoformat()[:10],
            "overall": self._empty_venue_stats(),
            "home": self._empty_venue_stats(),
            "away": self._empty_venue_stats(),
            "form_last_5": {"results": [], "form_string": "", "points": 0, "matches": 0},
            "form_last_10": {"results": [], "form_string": "", "points": 0, "matches": 0},
            "scoring_patterns": {},
            "by_month": {},
        }
    
    def _empty_venue_stats(self) -> Dict[str, Any]:
        """Return empty venue stats."""
        return {
            "matches": 0,
            "wins": 0,
            "draws": 0,
            "losses": 0,
            "goals_scored": 0,
            "goals_conceded": 0,
            "goals_scored_avg": 0.0,
            "goals_conceded_avg": 0.0,
            "win_rate": 0.0,
            "over25_rate": 0.0,
            "btts_rate": 0.0,
            "clean_sheet_rate": 0.0,
            "points": 0,
            "points_per_game": 0.0,
        }
