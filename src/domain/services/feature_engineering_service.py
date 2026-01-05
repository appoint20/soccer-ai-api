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
from src.domain.services.derby_service import DerbyService
from src.domain.services.match_stats_service import MatchStatsService
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
    - BTTS/Over25 venue-specific stats (NEW)
    
    Generates ~60 features per match for ML models.
    """
    
    def __init__(
        self,
        team_stats_service: Optional[TeamStatsService] = None,
        h2h_service: Optional[H2HService] = None,
        standings_service: Optional[StandingsService] = None,
        referee_stats_service: Optional[RefereeStatsService] = None,
        match_stats_service: Optional[MatchStatsService] = None,
        cache_manager: Optional[CacheManager] = None,
    ):
        """
        Initialize feature engineering service.
        
        Args:
            team_stats_service: Team stats service instance
            h2h_service: H2H service instance
            standings_service: Standings service instance
            referee_stats_service: Referee stats service instance
            match_stats_service: Match stats service for BTTS/Over25
            cache_manager: Optional cache manager
        """
        super().__init__(cache_manager)
        
        self.team_stats = team_stats_service or TeamStatsService(cache_manager)
        self.h2h = h2h_service or H2HService(cache_manager)
        self.standings = standings_service or StandingsService(cache_manager)
        self.referee_stats = referee_stats_service or RefereeStatsService(cache_manager)
        self.derby = DerbyService()  # Derby detection service
        self.match_stats = match_stats_service or MatchStatsService(cache_manager)  # NEW
        
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
            
            # Get H2H stats (Pass as_of_date for leakage prevention)
            h2h_stats = self.h2h.get_h2h_stats(
                home_team, away_team, all_matches, None, as_of_date
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
            
            # Get referee influence (Pass as_of_date for leakage prevention)
            referee_features = {}
            if referee:
                referee_features = self.referee_stats.get_referee_influence_features(
                    referee, all_matches, as_of_date
                )
            
            # Get BTTS/Over25 venue-specific stats (NEW)
            btts_o25_stats = self.match_stats.calculate_match_stats(
                home_team, away_team, all_matches, as_of_date, league
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
            
            # Odds features
            "odds_features": self._extract_odds_features(match),
            
            # Derby features
            "derby_features": self.derby.get_derby_features(home_team or "", away_team or ""),
            
            # BTTS/Over25 venue features (NEW - 12 features)
            "btts_over25_features": self._extract_btts_over25_features(btts_o25_stats),
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
            
            # Odds features
            "home_win_odds",
            "draw_odds",
            "away_win_odds",
            "over25_odds",
            "under25_odds",
            "implied_home_prob",
            "implied_over25_prob",
            "odds_value_home",
            "odds_value_over25",
            
            # BTTS features (NEW)
            "home_btts_l9_rate",
            "home_btts_home6_rate",
            "home_scored_l9_rate",
            "away_btts_l9_rate",
            "away_btts_away6_rate",
            "away_scored_l9_rate",
            
            # Over 2.5 features (NEW)
            "home_o25_l9_rate",
            "home_o25_home6_rate",
            "away_o25_l9_rate",
            "away_o25_away6_rate",
            
            # Qualification flags (NEW)
            "btts_qualified",
            "over25_qualified",
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
        
        # Odds features
        odds = features.get("odds_features", {})
        flat["home_win_odds"] = odds.get("home_win_odds", 2.5)
        flat["draw_odds"] = odds.get("draw_odds", 3.3)
        flat["away_win_odds"] = odds.get("away_win_odds", 3.0)
        flat["over25_odds"] = odds.get("over25_odds", 1.9)
        flat["under25_odds"] = odds.get("under25_odds", 1.9)
        flat["implied_home_prob"] = odds.get("implied_home_prob", 0.4)
        flat["implied_over25_prob"] = odds.get("implied_over25_prob", 0.5)
        flat["odds_value_home"] = odds.get("odds_value_home", 0.0)
        flat["odds_value_over25"] = odds.get("odds_value_over25", 0.0)
        
        # BTTS/Over25 venue features (NEW)
        btts_o25 = features.get("btts_over25_features", {})
        flat["home_btts_l9_rate"] = btts_o25.get("home_btts_l9_rate", 0.5)
        flat["home_btts_home6_rate"] = btts_o25.get("home_btts_home6_rate", 0.5)
        flat["home_scored_l9_rate"] = btts_o25.get("home_scored_l9_rate", 0.7)
        flat["away_btts_l9_rate"] = btts_o25.get("away_btts_l9_rate", 0.5)
        flat["away_btts_away6_rate"] = btts_o25.get("away_btts_away6_rate", 0.5)
        flat["away_scored_l9_rate"] = btts_o25.get("away_scored_l9_rate", 0.7)
        flat["home_o25_l9_rate"] = btts_o25.get("home_o25_l9_rate", 0.5)
        flat["home_o25_home6_rate"] = btts_o25.get("home_o25_home6_rate", 0.5)
        flat["away_o25_l9_rate"] = btts_o25.get("away_o25_l9_rate", 0.5)
        flat["away_o25_away6_rate"] = btts_o25.get("away_o25_away6_rate", 0.5)
        flat["btts_qualified"] = btts_o25.get("btts_qualified", 0.0)
        flat["over25_qualified"] = btts_o25.get("over25_qualified", 0.0)
        
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
    
    def _extract_odds_features(self, match: Dict[str, Any]) -> Dict[str, float]:
        """
        Extract betting odds features from match data.
        
        Calculates:
        - Raw odds for 1X2 and Over/Under markets
        - Implied probabilities (1/odds, normalized)
        - Value indicators (form-based prob - implied prob)
        
        Args:
            match: Match dict with odds columns (b365h, b365d, b365a, etc.)
            
        Returns:
            Dict with odds features
        """
        # Default odds (neutral market)
        home_odds = match.get("b365h") or match.get("home_win_odds") or 2.5
        draw_odds = match.get("b365d") or match.get("draw_odds") or 3.3
        away_odds = match.get("b365a") or match.get("away_win_odds") or 3.0
        over25_odds = match.get("b365_over25") or match.get("over25_odds") or 1.9
        under25_odds = match.get("b365_under25") or match.get("under25_odds") or 1.9
        
        # Ensure valid odds
        try:
            home_odds = float(home_odds) if home_odds else 2.5
            draw_odds = float(draw_odds) if draw_odds else 3.3
            away_odds = float(away_odds) if away_odds else 3.0
            over25_odds = float(over25_odds) if over25_odds else 1.9
            under25_odds = float(under25_odds) if under25_odds else 1.9
        except (ValueError, TypeError):
            home_odds, draw_odds, away_odds = 2.5, 3.3, 3.0
            over25_odds, under25_odds = 1.9, 1.9
        
        # Calculate implied probabilities (1/odds, then normalize to remove margin)
        raw_home_prob = 1.0 / home_odds if home_odds > 0 else 0.4
        raw_draw_prob = 1.0 / draw_odds if draw_odds > 0 else 0.3
        raw_away_prob = 1.0 / away_odds if away_odds > 0 else 0.3
        
        total_1x2 = raw_home_prob + raw_draw_prob + raw_away_prob
        implied_home_prob = raw_home_prob / total_1x2 if total_1x2 > 0 else 0.4
        
        raw_over25_prob = 1.0 / over25_odds if over25_odds > 0 else 0.5
        raw_under25_prob = 1.0 / under25_odds if under25_odds > 0 else 0.5
        
        total_ou = raw_over25_prob + raw_under25_prob
        implied_over25_prob = raw_over25_prob / total_ou if total_ou > 0 else 0.5
        
        # Value calculation: compare to form-based estimates
        # Positive value = our model thinks probability is higher than bookmaker
        # We'll use match context features later; for now, estimate from odds spread
        avg_home_prob = 0.45  # Base home advantage
        odds_value_home = avg_home_prob - implied_home_prob
        odds_value_over25 = 0.5 - implied_over25_prob  # Neutral baseline
        
        return {
            "home_win_odds": round_to_precision(home_odds),
            "draw_odds": round_to_precision(draw_odds),
            "away_win_odds": round_to_precision(away_odds),
            "over25_odds": round_to_precision(over25_odds),
            "under25_odds": round_to_precision(under25_odds),
            "implied_home_prob": round_to_precision(implied_home_prob),
            "implied_over25_prob": round_to_precision(implied_over25_prob),
            "odds_value_home": round_to_precision(odds_value_home),
            "odds_value_over25": round_to_precision(odds_value_over25),
        }
    
    def _extract_btts_over25_features(self, btts_o25_stats: Dict[str, Any]) -> Dict[str, float]:
        """
        Extract BTTS and Over 2.5 venue-specific features.
        
        Features:
        - Home team BTTS/Over25 last 9 overall
        - Home team BTTS/Over25 last 6 at home
        - Away team BTTS/Over25 last 9 overall  
        - Away team BTTS/Over25 last 6 away
        - Home/Away scored rates
        - Qualification flags as binary
        
        Args:
            btts_o25_stats: Stats from MatchStatsService
            
        Returns:
            Dict with 12 BTTS/Over25 features
        """
        btts = btts_o25_stats.get("btts", {})
        over25 = btts_o25_stats.get("over25", {})
        qualification = btts_o25_stats.get("qualification", {})
        
        home_btts = btts.get("home_team", {})
        away_btts = btts.get("away_team", {})
        home_o25 = over25.get("home_team", {})
        away_o25 = over25.get("away_team", {})
        
        return {
            # BTTS features (6)
            "home_btts_l9_rate": home_btts.get("overall_9", {}).get("pct", 50.0) / 100.0,
            "home_btts_home6_rate": home_btts.get("home_6", {}).get("pct", 50.0) / 100.0,
            "home_scored_l9_rate": home_btts.get("scored_overall_9", {}).get("pct", 70.0) / 100.0,
            "away_btts_l9_rate": away_btts.get("overall_9", {}).get("pct", 50.0) / 100.0,
            "away_btts_away6_rate": away_btts.get("away_6", {}).get("pct", 50.0) / 100.0,
            "away_scored_l9_rate": away_btts.get("scored_overall_9", {}).get("pct", 70.0) / 100.0,
            
            # Over 2.5 features (4)
            "home_o25_l9_rate": home_o25.get("overall_9", {}).get("pct", 50.0) / 100.0,
            "home_o25_home6_rate": home_o25.get("home_6", {}).get("pct", 50.0) / 100.0,
            "away_o25_l9_rate": away_o25.get("overall_9", {}).get("pct", 50.0) / 100.0,
            "away_o25_away6_rate": away_o25.get("away_6", {}).get("pct", 50.0) / 100.0,
            
            # Qualification flags as binary (2)
            "btts_qualified": 1.0 if qualification.get("btts_qualified", False) else 0.0,
            "over25_qualified": 1.0 if qualification.get("over25_qualified", False) else 0.0,
        }
