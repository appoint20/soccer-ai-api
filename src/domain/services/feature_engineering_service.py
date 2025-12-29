"""
Feature engineering service - master service combining all features.

Coordinates all other services to generate comprehensive feature
vectors for ML models.
"""
from datetime import date, datetime
from typing import Optional, List, Dict, Any

from src.domain.services.base_service import BaseService
from src.domain.services.team_stats_service import TeamStatsService
from src.domain.services.h2h_service import H2HService
from src.domain.services.standings_service import StandingsService
from src.domain.services.referee_stats_service import RefereeStatsService
from src.data.cache.cache_manager import CacheManager
from src.utils.stats_utils import round_to_precision
from src.utils.date_utils import get_season_of_year, is_festive_period


class FeatureEngineeringService(BaseService):
    """
    Master service for feature engineering.
    
    Combines features from:
    - Team statistics (home/away)
    - Head-to-head records
    - League standings
    - Referee tendencies
    - Match context
    
    Generates ~48 features per match for ML models.
    """
    
    def __init__(
        self,
        team_stats_service: Optional[TeamStatsService] = None,
        h2h_service: Optional[H2HService] = None,
        standings_service: Optional[StandingsService] = None,
        referee_stats_service: Optional[RefereeStatsService] = None,
        cache_manager: Optional[CacheManager] = None,
    ):
        """
        Initialize feature engineering service.
        
        Args:
            team_stats_service: Team stats service instance
            h2h_service: H2H service instance
            standings_service: Standings service instance
            referee_stats_service: Referee stats service instance
            cache_manager: Optional cache manager
        """
        super().__init__(cache_manager)
        
        self.team_stats = team_stats_service or TeamStatsService(cache_manager)
        self.h2h = h2h_service or H2HService(cache_manager)
        self.standings = standings_service or StandingsService(cache_manager)
        self.referee_stats = referee_stats_service or RefereeStatsService(cache_manager)
        
        self._feature_names: List[str] = []
    
    def generate_features_for_match(
        self,
        match: Dict[str, Any],
        all_matches: List,
        as_of_date: Optional[date] = None,
    ) -> Dict[str, Any]:
        """
        Generate complete feature set for a single match.
        
        Args:
            match: Match dict with home_team, away_team, date, etc.
            all_matches: All historical matches for context
            as_of_date: Date for time-travel (use day before match)
            
        Returns:
            Dict with all features (~48 features)
        """
        home_team = match.get("home_team")
        away_team = match.get("away_team")
        match_date = match.get("match_date")
        league = match.get("league")
        referee = match.get("referee")
        
        # Determine as_of_date for time-travel
        if as_of_date is None and match_date:
            if isinstance(match_date, str):
                try:
                    match_date = datetime.fromisoformat(match_date[:10]).date()
                except (ValueError, TypeError):
                    pass
            if isinstance(match_date, date):
                as_of_date = match_date
        
        with self.track_performance("generate_features"):
            # Get team stats
            home_stats = self.team_stats.calculate_team_stats(
                home_team, all_matches, as_of_date, league
            )
            away_stats = self.team_stats.calculate_team_stats(
                away_team, all_matches, as_of_date, league
            )
            
            # Get H2H stats
            h2h_stats = self.h2h.get_h2h_stats(
                home_team, away_team, all_matches
            )
            
            # Get standings context
            home_context = {}
            away_context = {}
            if league and as_of_date:
                home_context = self.standings.calculate_position_context(
                    home_team, all_matches, league, as_of_date
                )
                away_context = self.standings.calculate_position_context(
                    away_team, all_matches, league, as_of_date
                )
            
            # Get referee influence
            referee_features = {}
            if referee:
                referee_features = self.referee_stats.get_referee_influence_features(
                    referee, all_matches
                )
        
        # Build feature vector
        features = {
            "match_id": self._generate_match_id(match),
            "date": str(match_date)[:10] if match_date else "",
            "league": league or "",
            "home_team": home_team or "",
            "away_team": away_team or "",
            
            # Home team features (~15)
            "home_features": self._extract_team_features(home_stats, is_home=True),
            
            # Away team features (~15)
            "away_features": self._extract_team_features(away_stats, is_home=False),
            
            # H2H features (~10)
            "h2h_features": self._extract_h2h_features(h2h_stats),
            
            # Referee features
            "referee_features": self._extract_referee_features(referee_features, referee),
            
            # Match context (~8)
            "match_context": self._calculate_match_context(
                match, home_stats, away_stats, home_context, away_context
            ),
        }
        
        return features
    
    def generate_training_features(
        self,
        matches: List,
        include_targets: bool = True,
    ) -> List[Dict[str, Any]]:
        """
        Generate features for all historical matches.
        
        Args:
            matches: All historical matches
            include_targets: Include target variables (over25, btts, result)
            
        Returns:
            List of feature dicts with targets
        """
        self.logger.info(f"Generating features for {len(matches)} matches")
        
        features_list = []
        
        for i, match in enumerate(matches):
            if isinstance(match, dict):
                match_dict = match
            else:
                match_dict = {
                    "home_team": getattr(match, "home_team", None),
                    "away_team": getattr(match, "away_team", None),
                    "match_date": getattr(match, "match_date", None),
                    "league": getattr(match, "league", None),
                    "referee": getattr(match, "referee", None),
                    "fthg": getattr(match, "fthg", None),
                    "ftag": getattr(match, "ftag", None),
                    "ftr": getattr(match, "ftr", None),
                }
            
            features = self.generate_features_for_match(match_dict, matches)
            
            # Add target variables
            if include_targets:
                fthg = match_dict.get("fthg")
                ftag = match_dict.get("ftag")
                ftr = match_dict.get("ftr")
                
                if fthg is not None and ftag is not None:
                    total_goals = fthg + ftag
                    features["targets"] = {
                        "over25": total_goals > 2.5,
                        "btts": fthg > 0 and ftag > 0,
                        "result": ftr,
                        "total_goals": total_goals,
                        "home_goals": fthg,
                        "away_goals": ftag,
                    }
                else:
                    features["targets"] = None
            
            features_list.append(features)
            
            if (i + 1) % 500 == 0:
                self.logger.info(f"Generated features for {i + 1}/{len(matches)} matches")
        
        return features_list
    
    def generate_prediction_features(
        self,
        upcoming_matches: List,
        historical_matches: List,
    ) -> List[Dict[str, Any]]:
        """
        Generate features for upcoming matches.
        
        Args:
            upcoming_matches: Matches to predict
            historical_matches: All historical data
            
        Returns:
            List of feature dicts (no targets)
        """
        features_list = []
        
        for match in upcoming_matches:
            if isinstance(match, dict):
                match_dict = match
            else:
                match_dict = {
                    "home_team": getattr(match, "home_team", None),
                    "away_team": getattr(match, "away_team", None),
                    "match_date": getattr(match, "match_date", None),
                    "league": getattr(match, "league", None),
                    "referee": getattr(match, "referee", None),
                }
            
            features = self.generate_features_for_match(
                match_dict, historical_matches
            )
            features["targets"] = None  # No targets for predictions
            
            features_list.append(features)
        
        return features_list
    
    def get_feature_names(self) -> List[str]:
        """
        Return list of all feature names in order.
        
        Returns:
            List of feature names for ML model
        """
        return [
            # Home features
            "home_goals_scored_avg_season",
            "home_goals_scored_avg_venue",
            "home_goals_scored_avg_l5",
            "home_goals_conceded_avg_season",
            "home_goals_conceded_avg_venue",
            "home_goals_conceded_avg_l5",
            "home_over25_rate_season",
            "home_over25_rate_l5",
            "home_btts_rate_season",
            "home_btts_rate_l5",
            "home_win_rate_venue",
            "home_position",
            "home_points",
            "home_points_l5",
            "home_form_trend",
            
            # Away features
            "away_goals_scored_avg_season",
            "away_goals_scored_avg_venue",
            "away_goals_scored_avg_l5",
            "away_goals_conceded_avg_season",
            "away_goals_conceded_avg_venue",
            "away_goals_conceded_avg_l5",
            "away_over25_rate_season",
            "away_over25_rate_l5",
            "away_btts_rate_season",
            "away_btts_rate_l5",
            "away_win_rate_venue",
            "away_position",
            "away_points",
            "away_points_l5",
            "away_form_trend",
            
            # H2H features
            "h2h_meetings",
            "h2h_over25_rate",
            "h2h_btts_rate",
            "h2h_home_win_rate",
            "h2h_weighted_over25",
            "h2h_weighted_btts",
            "h2h_avg_total_goals",
            
            # Referee features
            "referee_over25_rate",
            "referee_btts_rate",
            "referee_avg_goals",
            "referee_avg_cards",
            
            # Context features
            "season_of_year",
            "position_diff",
            "points_diff",
            "expected_total_goals",
            "is_festive_period",
        ]
    
    def flatten_features(self, features: Dict[str, Any]) -> Dict[str, float]:
        """
        Flatten nested features to single-level dict for ML.
        
        Args:
            features: Nested feature dict
            
        Returns:
            Flattened dict with numeric values
        """
        flat = {}
        
        # Home features
        home = features.get("home_features", {})
        flat["home_goals_scored_avg_season"] = home.get("goals_scored_avg_season", 0.0)
        flat["home_goals_scored_avg_venue"] = home.get("goals_scored_avg_venue", 0.0)
        flat["home_goals_scored_avg_l5"] = home.get("goals_scored_avg_l5", 0.0)
        flat["home_goals_conceded_avg_season"] = home.get("goals_conceded_avg_season", 0.0)
        flat["home_goals_conceded_avg_venue"] = home.get("goals_conceded_avg_venue", 0.0)
        flat["home_goals_conceded_avg_l5"] = home.get("goals_conceded_avg_l5", 0.0)
        flat["home_over25_rate_season"] = home.get("over25_rate_season", 0.5)
        flat["home_over25_rate_l5"] = home.get("over25_rate_l5", 0.5)
        flat["home_btts_rate_season"] = home.get("btts_rate_season", 0.5)
        flat["home_btts_rate_l5"] = home.get("btts_rate_l5", 0.5)
        flat["home_win_rate_venue"] = home.get("win_rate_venue", 0.5)
        flat["home_position"] = home.get("position", 10)
        flat["home_points"] = home.get("points", 0)
        flat["home_points_l5"] = home.get("points_l5", 5)
        flat["home_form_trend"] = self._encode_trend(home.get("form_trend", "stable"))
        
        # Away features
        away = features.get("away_features", {})
        flat["away_goals_scored_avg_season"] = away.get("goals_scored_avg_season", 0.0)
        flat["away_goals_scored_avg_venue"] = away.get("goals_scored_avg_venue", 0.0)
        flat["away_goals_scored_avg_l5"] = away.get("goals_scored_avg_l5", 0.0)
        flat["away_goals_conceded_avg_season"] = away.get("goals_conceded_avg_season", 0.0)
        flat["away_goals_conceded_avg_venue"] = away.get("goals_conceded_avg_venue", 0.0)
        flat["away_goals_conceded_avg_l5"] = away.get("goals_conceded_avg_l5", 0.0)
        flat["away_over25_rate_season"] = away.get("over25_rate_season", 0.5)
        flat["away_over25_rate_l5"] = away.get("over25_rate_l5", 0.5)
        flat["away_btts_rate_season"] = away.get("btts_rate_season", 0.5)
        flat["away_btts_rate_l5"] = away.get("btts_rate_l5", 0.5)
        flat["away_win_rate_venue"] = away.get("win_rate_venue", 0.5)
        flat["away_position"] = away.get("position", 10)
        flat["away_points"] = away.get("points", 0)
        flat["away_points_l5"] = away.get("points_l5", 5)
        flat["away_form_trend"] = self._encode_trend(away.get("form_trend", "stable"))
        
        # H2H features
        h2h = features.get("h2h_features", {})
        flat["h2h_meetings"] = h2h.get("meetings", 0)
        flat["h2h_over25_rate"] = h2h.get("over25_rate", 0.5)
        flat["h2h_btts_rate"] = h2h.get("btts_rate", 0.5)
        flat["h2h_home_win_rate"] = h2h.get("home_win_rate", 0.33)
        flat["h2h_weighted_over25"] = h2h.get("weighted_over25", 0.5)
        flat["h2h_weighted_btts"] = h2h.get("weighted_btts", 0.5)
        flat["h2h_avg_total_goals"] = h2h.get("avg_total_goals", 2.5)
        
        # Referee features
        ref = features.get("referee_features", {})
        flat["referee_over25_rate"] = ref.get("over25_rate", 0.5)
        flat["referee_btts_rate"] = ref.get("btts_rate", 0.5)
        flat["referee_avg_goals"] = ref.get("avg_goals", 2.5)
        flat["referee_avg_cards"] = ref.get("avg_cards", 4.0)
        
        # Context features
        ctx = features.get("match_context", {})
        flat["season_of_year"] = self._encode_season(ctx.get("season_of_year", "Autumn"))
        flat["position_diff"] = ctx.get("position_diff", 0)
        flat["points_diff"] = ctx.get("points_diff", 0)
        flat["expected_total_goals"] = ctx.get("expected_total_goals", 2.5)
        flat["is_festive_period"] = 1.0 if ctx.get("is_festive_period", False) else 0.0
        
        return flat
    
    def _extract_team_features(
        self,
        team_stats: Dict[str, Any],
        is_home: bool,
    ) -> Dict[str, Any]:
        """Extract features from team stats."""
        overall = team_stats.get("overall", {})
        venue = team_stats.get("home" if is_home else "away", {})
        form5 = team_stats.get("form_last_5", {})
        
        return {
            "goals_scored_avg_season": overall.get("goals_scored_avg", 0.0),
            "goals_scored_avg_venue": venue.get("goals_scored_avg", 0.0),
            "goals_scored_avg_l5": form5.get("goals_scored_avg", 0.0),
            "goals_conceded_avg_season": overall.get("goals_conceded_avg", 0.0),
            "goals_conceded_avg_venue": venue.get("goals_conceded_avg", 0.0),
            "goals_conceded_avg_l5": form5.get("goals_conceded_avg", 0.0),
            "over25_rate_season": overall.get("over25_rate", 0.5),
            "over25_rate_l5": form5.get("over25_rate", 0.5),
            "btts_rate_season": overall.get("btts_rate", 0.5),
            "btts_rate_l5": form5.get("btts_rate", 0.5),
            "win_rate_venue": venue.get("win_rate", 0.5),
            "position": 10,  # Will be overridden with standings data
            "points": overall.get("points", 0),
            "points_l5": form5.get("points", 5),
            "form_trend": form5.get("trend", "stable"),
            "form_string": form5.get("form_string", ""),
            "matches_played": overall.get("matches", 0),
        }
    
    def _extract_h2h_features(
        self,
        h2h_stats: Dict[str, Any],
    ) -> Dict[str, Any]:
        """Extract features from H2H stats."""
        goal_stats = h2h_stats.get("goal_statistics", {})
        weighted = h2h_stats.get("weighted_stats", {})
        record = h2h_stats.get("overall_record", {})
        
        home_wins = record.get("home_wins", 0)
        total = h2h_stats.get("total_meetings", 0)
        home_win_rate = home_wins / total if total > 0 else 0.33
        
        return {
            "meetings": total,
            "over25_rate": goal_stats.get("over25_rate", 0.5),
            "btts_rate": goal_stats.get("btts_rate", 0.5),
            "home_win_rate": round_to_precision(home_win_rate),
            "weighted_over25": weighted.get("over25_probability", 0.5),
            "weighted_btts": weighted.get("btts_probability", 0.5),
            "avg_total_goals": goal_stats.get("avg_total_goals", 2.5),
            "home_goals_avg": record.get("home_goals_avg", 1.25),
            "away_goals_avg": record.get("away_goals_avg", 1.25),
        }
    
    def _extract_referee_features(
        self,
        referee_influence: Dict[str, Any],
        referee_name: Optional[str],
    ) -> Dict[str, Any]:
        """Extract referee features."""
        if not referee_influence:
            return {
                "name": referee_name or "Unknown",
                "over25_rate": 0.5,
                "btts_rate": 0.5,
                "avg_goals": 2.5,
                "avg_cards": 4.0,
                "style": "moderate",
                "reliable": False,
            }
        
        return {
            "name": referee_name or "Unknown",
            "over25_rate": referee_influence.get("over25_rate", 0.5),
            "btts_rate": referee_influence.get("btts_rate", 0.5),
            "avg_goals": referee_influence.get("avg_goals", 2.5),
            "avg_cards": referee_influence.get("avg_cards", 4.0),
            "style": referee_influence.get("card_tendency", "moderate"),
            "reliable": referee_influence.get("reliable", False),
        }
    
    def _calculate_match_context(
        self,
        match: Dict[str, Any],
        home_stats: Dict[str, Any],
        away_stats: Dict[str, Any],
        home_context: Dict[str, Any],
        away_context: Dict[str, Any],
    ) -> Dict[str, Any]:
        """Calculate match context features."""
        match_date = match.get("match_date")
        
        # Season of year
        season = "Autumn"
        festive = False
        if match_date:
            if isinstance(match_date, str):
                try:
                    match_date = datetime.fromisoformat(match_date[:10]).date()
                except:
                    pass
            if isinstance(match_date, date):
                season = get_season_of_year(match_date)
                festive = is_festive_period(match_date)
        
        # Position and points diff
        home_pos = home_context.get("position", 10)
        away_pos = away_context.get("position", 10)
        home_pts = home_context.get("points", 0)
        away_pts = away_context.get("points", 0)
        
        # Expected goals
        home_overall = home_stats.get("overall", {})
        away_overall = away_stats.get("overall", {})
        home_scored = home_stats.get("home", {}).get("goals_scored_avg", 1.3)
        away_scored = away_stats.get("away", {}).get("goals_scored_avg", 1.2)
        expected_goals = home_scored + away_scored
        
        # Form check
        home_form = home_stats.get("form_last_5", {})
        away_form = away_stats.get("form_last_5", {})
        home_in_form = home_form.get("points", 0) >= 10  # 10+ points from 5 matches
        away_in_form = away_form.get("points", 0) >= 10
        
        return {
            "season_of_year": season,
            "month": match_date.month if isinstance(match_date, date) else 0,
            "position_diff": home_pos - away_pos,
            "points_diff": home_pts - away_pts,
            "expected_total_goals": round_to_precision(expected_goals),
            "home_in_form": home_in_form,
            "away_in_form": away_in_form,
            "is_festive_period": festive,
        }
    
    def _generate_match_id(self, match: Dict[str, Any]) -> str:
        """Generate unique match ID."""
        league = match.get("league", "XX")
        date_str = str(match.get("match_date", ""))[:10]
        home = match.get("home_team", "").replace(" ", "")
        away = match.get("away_team", "").replace(" ", "")
        return f"{league}_{date_str}_{home}_{away}"
    
    def _encode_trend(self, trend: str) -> float:
        """Encode trend to numeric."""
        if trend == "improving":
            return 1.0
        elif trend == "declining":
            return -1.0
        return 0.0
    
    def _encode_season(self, season: str) -> float:
        """Encode season to numeric (0-3)."""
        seasons = {"Winter": 0, "Spring": 1, "Summer": 2, "Autumn": 3}
        return float(seasons.get(season, 3))
