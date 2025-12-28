"""
Unit tests for domain entities: Match, Team, Prediction.

Tests cover:
- Object creation with all fields
- Object creation with minimal fields
- Computed properties
- Serialization/deserialization
- Edge cases and validation
"""
import pytest
from datetime import date, time, datetime

import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent.parent.parent))

from src.domain.entities import Match, Team, Prediction, TeamStats, HomeAwayStats
from src.domain.entities.prediction import ResultProbabilities


class TestMatchEntity:
    """Tests for Match entity."""
    
    def test_create_match_with_all_fields(self, sample_match_home_win):
        """Test creating a Match with all fields."""
        match = Match(
            id=sample_match_home_win["id"],
            home_team=sample_match_home_win["home_team"],
            away_team=sample_match_home_win["away_team"],
            match_date=sample_match_home_win["match_date"],
            match_time=sample_match_home_win["match_time"],
            league=sample_match_home_win["league"],
            season=sample_match_home_win["season"],
            fthg=sample_match_home_win["fthg"],
            ftag=sample_match_home_win["ftag"],
            ftr=sample_match_home_win["ftr"],
        )
        
        assert match.home_team == "Arsenal"
        assert match.away_team == "Chelsea"
        assert match.fthg == 3
        assert match.ftag == 1
        assert match.ftr == "H"
    
    def test_create_match_minimal_fields(self):
        """Test creating a Match with minimal required fields."""
        match = Match(
            home_team="Arsenal",
            away_team="Chelsea",
            match_date=date(2024, 9, 15),
            league="E0",
            season="2024-25",
        )
        
        assert match.home_team == "Arsenal"
        assert match.fthg is None
        assert match.is_completed is False
    
    def test_total_goals_calculation(self):
        """Test total_goals property calculates correctly."""
        match = Match(
            home_team="Arsenal",
            away_team="Chelsea",
            match_date=date(2024, 9, 15),
            league="E0",
            season="2024-25",
            fthg=3,
            ftag=1,
            ftr="H",
        )
        
        assert match.total_goals == 4
    
    def test_total_goals_none_when_no_result(self):
        """Test total_goals returns None for upcoming matches."""
        match = Match(
            home_team="Arsenal",
            away_team="Chelsea",
            match_date=date(2024, 9, 15),
            league="E0",
            season="2024-25",
        )
        
        assert match.total_goals is None
    
    def test_is_over25_true(self):
        """Test is_over_25 returns True for >2.5 goals."""
        match = Match(
            home_team="Arsenal",
            away_team="Chelsea",
            match_date=date(2024, 9, 15),
            league="E0",
            season="2024-25",
            fthg=2,
            ftag=1,
            ftr="H",
        )
        
        assert match.is_over_25 is True
    
    def test_is_over25_false(self):
        """Test is_over_25 returns False for <=2.5 goals."""
        match = Match(
            home_team="Arsenal",
            away_team="Chelsea",
            match_date=date(2024, 9, 15),
            league="E0",
            season="2024-25",
            fthg=1,
            ftag=1,
            ftr="D",
        )
        
        assert match.is_over_25 is False
    
    def test_is_over25_edge_case_zero_zero(self, sample_match_zero_zero):
        """Test is_over_25 for 0-0 match."""
        match = Match(
            home_team=sample_match_zero_zero["home_team"],
            away_team=sample_match_zero_zero["away_team"],
            match_date=sample_match_zero_zero["match_date"],
            league=sample_match_zero_zero["league"],
            season=sample_match_zero_zero["season"],
            fthg=sample_match_zero_zero["fthg"],
            ftag=sample_match_zero_zero["ftag"],
            ftr=sample_match_zero_zero["ftr"],
        )
        
        assert match.total_goals == 0
        assert match.is_over_25 is False
    
    def test_is_btts_true(self):
        """Test is_btts returns True when both teams score."""
        match = Match(
            home_team="Arsenal",
            away_team="Chelsea",
            match_date=date(2024, 9, 15),
            league="E0",
            season="2024-25",
            fthg=2,
            ftag=1,
            ftr="H",
        )
        
        assert match.is_btts is True
    
    def test_is_btts_false_no_away_goals(self):
        """Test is_btts returns False when away team doesn't score."""
        match = Match(
            home_team="Arsenal",
            away_team="Chelsea",
            match_date=date(2024, 9, 15),
            league="E0",
            season="2024-25",
            fthg=2,
            ftag=0,
            ftr="H",
        )
        
        assert match.is_btts is False
    
    def test_is_btts_false_zero_zero(self, sample_match_zero_zero):
        """Test is_btts for 0-0 match."""
        match = Match(
            home_team=sample_match_zero_zero["home_team"],
            away_team=sample_match_zero_zero["away_team"],
            match_date=sample_match_zero_zero["match_date"],
            league=sample_match_zero_zero["league"],
            season=sample_match_zero_zero["season"],
            fthg=0,
            ftag=0,
            ftr="D",
        )
        
        assert match.is_btts is False
    
    def test_is_completed_true(self):
        """Test is_completed returns True when ftr is set."""
        match = Match(
            home_team="Arsenal",
            away_team="Chelsea",
            match_date=date(2024, 9, 15),
            league="E0",
            season="2024-25",
            fthg=2,
            ftag=1,
            ftr="H",
        )
        
        assert match.is_completed is True
    
    def test_is_completed_false(self):
        """Test is_completed returns False for upcoming match."""
        match = Match(
            home_team="Arsenal",
            away_team="Chelsea",
            match_date=date(2024, 9, 15),
            league="E0",
            season="2024-25",
        )
        
        assert match.is_completed is False
    
    def test_match_key_generation(self):
        """Test match_key property generates unique identifier."""
        match = Match(
            home_team="Arsenal",
            away_team="Chelsea",
            match_date=date(2024, 9, 15),
            league="E0",
            season="2024-25",
        )
        
        key = match.match_key
        assert "2024-09-15" in key
        assert "Arsenal" in key
        assert "Chelsea" in key
        assert "E0" in key
    
    def test_to_dict_serialization(self, sample_match):
        """Test Match serializes to dictionary correctly."""
        data = sample_match.to_dict()
        
        assert isinstance(data, dict)
        assert data["home_team"] == "Arsenal"
        assert data["match_date"] == "2024-09-15"
        assert data["match_time"] == "15:00"
        assert data["fthg"] == 3
    
    def test_from_dict_deserialization(self, sample_match):
        """Test Match deserializes from dictionary correctly."""
        data = sample_match.to_dict()
        match2 = Match.from_dict(data)
        
        assert match2.home_team == sample_match.home_team
        assert match2.match_date == sample_match.match_date
        assert match2.fthg == sample_match.fthg
    
    def test_match_with_missing_optional_fields(self, sample_match_missing_optional):
        """Test Match handles missing optional fields."""
        match = Match(
            home_team=sample_match_missing_optional["home_team"],
            away_team=sample_match_missing_optional["away_team"],
            match_date=sample_match_missing_optional["match_date"],
            league=sample_match_missing_optional["league"],
            season=sample_match_missing_optional["season"],
            fthg=sample_match_missing_optional["fthg"],
            ftag=sample_match_missing_optional["ftag"],
            ftr=sample_match_missing_optional["ftr"],
        )
        
        assert match.referee is None
        assert match.hs is None
        assert match.match_time is None
        assert match.is_completed is True
    
    def test_match_high_scoring(self, sample_match_high_scoring):
        """Test Match with high-scoring game."""
        match = Match(
            home_team=sample_match_high_scoring["home_team"],
            away_team=sample_match_high_scoring["away_team"],
            match_date=sample_match_high_scoring["match_date"],
            league=sample_match_high_scoring["league"],
            season=sample_match_high_scoring["season"],
            fthg=sample_match_high_scoring["fthg"],
            ftag=sample_match_high_scoring["ftag"],
            ftr=sample_match_high_scoring["ftr"],
        )
        
        assert match.total_goals == 7
        assert match.is_over_25 is True
        assert match.is_btts is True
    
    def test_match_repr_completed(self, sample_match):
        """Test Match string representation for completed match."""
        repr_str = repr(sample_match)
        assert "Arsenal" in repr_str
        assert "Chelsea" in repr_str
        assert "3" in repr_str
        assert "1" in repr_str
    
    def test_match_repr_upcoming(self):
        """Test Match string representation for upcoming match."""
        match = Match(
            home_team="Arsenal",
            away_team="Chelsea",
            match_date=date(2024, 9, 15),
            league="E0",
            season="2024-25",
        )
        repr_str = repr(match)
        assert "Arsenal" in repr_str
        assert "vs" in repr_str


class TestTeamEntity:
    """Tests for Team entity."""
    
    def test_create_team_with_all_fields(self, sample_team):
        """Test creating a Team with all fields."""
        assert sample_team.name == "Arsenal"
        assert sample_team.league == "E0"
        assert sample_team.current_position == 3
    
    def test_create_team_minimal(self):
        """Test creating a Team with minimal fields."""
        team = Team(name="Chelsea", league="E0")
        
        assert team.name == "Chelsea"
        assert team.stats.total_matches == 0
        assert team.current_position is None
    
    def test_team_stats_goals_scored_avg(self, sample_team):
        """Test goals scored average calculation."""
        # Home: 18 scored in 10 matches = 1.8
        # Away: 12 scored in 10 matches = 1.2
        # Total: 30 in 20 matches = 1.5
        assert sample_team.stats.home.goals_scored_avg == 1.8
        assert sample_team.stats.away.goals_scored_avg == 1.2
        assert sample_team.stats.goals_scored_avg == 1.5
    
    def test_team_stats_goals_conceded_avg(self, sample_team):
        """Test goals conceded average calculation."""
        # Home: 8 conceded in 10 matches = 0.8
        # Away: 10 conceded in 10 matches = 1.0
        # Total: 18 in 20 matches = 0.9
        assert sample_team.stats.home.goals_conceded_avg == 0.8
        assert sample_team.stats.away.goals_conceded_avg == 1.0
        assert sample_team.stats.goals_conceded_avg == 0.9
    
    def test_team_over25_rate(self, sample_team):
        """Test over 2.5 rate calculation."""
        # Home: 6/10 = 0.6
        # Away: 5/10 = 0.5
        # Total: 11/20 = 0.55
        assert sample_team.stats.home.over_25_rate == 0.6
        assert sample_team.stats.away.over_25_rate == 0.5
        assert sample_team.stats.over_25_rate == 0.55
    
    def test_team_btts_rate(self, sample_team):
        """Test BTTS rate calculation."""
        # Home: 5/10 = 0.5
        # Away: 6/10 = 0.6
        # Total: 11/20 = 0.55
        assert sample_team.stats.home.btts_rate == 0.5
        assert sample_team.stats.away.btts_rate == 0.6
        assert sample_team.stats.btts_rate == 0.55
    
    def test_team_win_rate(self, sample_team):
        """Test win rate calculation."""
        # Home: 6/10 = 0.6
        # Away: 4/10 = 0.4
        assert sample_team.stats.home.win_rate == 0.6
        assert sample_team.stats.away.win_rate == 0.4
    
    def test_team_form_string(self, sample_team):
        """Test form string generation."""
        assert sample_team.form_string == "WWDLW"
    
    def test_team_form_points(self, sample_team):
        """Test form points calculation."""
        # W=3, W=3, D=1, L=0, W=3 = 10
        assert sample_team.form_points == 10
    
    def test_team_add_result(self):
        """Test adding match result to team."""
        team = Team(name="Arsenal", league="E0")
        
        team.add_result("W")
        team.add_result("D")
        team.add_result("L")
        
        assert team.last_5_results == ["W", "D", "L"]
        assert team.form_string == "WDL"
    
    def test_team_to_dict(self, sample_team):
        """Test Team serialization."""
        data = sample_team.to_dict()
        
        assert data["name"] == "Arsenal"
        assert data["league"] == "E0"
        assert "stats" in data
        assert "home" in data["stats"]
    
    def test_team_from_dict(self, sample_team):
        """Test Team deserialization."""
        data = sample_team.to_dict()
        team2 = Team.from_dict(data)
        
        assert team2.name == sample_team.name
        assert team2.stats.total_matches == sample_team.stats.total_matches
    
    def test_team_empty_stats_averages(self):
        """Test stats averages with no matches."""
        team = Team(name="NewTeam", league="E0")
        
        assert team.stats.goals_scored_avg == 0.0
        assert team.stats.over_25_rate == 0.0


class TestPredictionEntity:
    """Tests for Prediction entity."""
    
    def test_create_prediction_with_all_fields(self, sample_prediction):
        """Test creating a Prediction with all fields."""
        assert sample_prediction.match_id == "match-001"
        assert sample_prediction.over25_prediction is True
        assert sample_prediction.over25_probability == 0.72
        assert sample_prediction.over25_confidence == "high"
    
    def test_create_prediction_minimal(self):
        """Test creating a Prediction with minimal fields."""
        pred = Prediction(match_id="match-001")
        
        assert pred.match_id == "match-001"
        assert pred.over25_prediction is False
        assert pred.is_verified is False
    
    def test_prediction_is_verified(self, sample_prediction):
        """Test is_verified property."""
        assert sample_prediction.is_verified is False
        
        sample_prediction.set_actual_results(True, True, "H")
        assert sample_prediction.is_verified is True
    
    def test_prediction_over25_correct(self, sample_prediction):
        """Test over25_correct property."""
        sample_prediction.set_actual_results(True, True, "H")
        assert sample_prediction.over25_correct is True
        
        sample_prediction.actual_over25 = False
        assert sample_prediction.over25_correct is False
    
    def test_prediction_btts_correct(self, sample_prediction):
        """Test btts_correct property."""
        sample_prediction.set_actual_results(True, True, "H")
        assert sample_prediction.btts_correct is True
        
        sample_prediction.actual_btts = False
        assert sample_prediction.btts_correct is False
    
    def test_prediction_result_correct(self, sample_prediction):
        """Test result_correct property."""
        sample_prediction.set_actual_results(True, True, "H")
        assert sample_prediction.result_correct is True
        
        sample_prediction.actual_result = "D"
        assert sample_prediction.result_correct is False
    
    def test_confidence_calculation_high(self):
        """Test confidence calculation for high probability."""
        assert Prediction.calculate_confidence(0.75) == "high"
        assert Prediction.calculate_confidence(0.70) == "high"
    
    def test_confidence_calculation_medium(self):
        """Test confidence calculation for medium probability."""
        assert Prediction.calculate_confidence(0.65) == "medium"
        assert Prediction.calculate_confidence(0.55) == "medium"
    
    def test_confidence_calculation_low(self):
        """Test confidence calculation for low probability."""
        assert Prediction.calculate_confidence(0.50) == "low"
        assert Prediction.calculate_confidence(0.40) == "low"
    
    def test_result_probabilities_predicted_result(self):
        """Test predicted result from probabilities."""
        probs = ResultProbabilities(home_win=0.55, draw=0.25, away_win=0.20)
        assert probs.predicted_result == "H"
        
        probs2 = ResultProbabilities(home_win=0.20, draw=0.25, away_win=0.55)
        assert probs2.predicted_result == "A"
        
        probs3 = ResultProbabilities(home_win=0.30, draw=0.40, away_win=0.30)
        assert probs3.predicted_result == "D"
    
    def test_result_probabilities_max(self):
        """Test max probability."""
        probs = ResultProbabilities(home_win=0.55, draw=0.25, away_win=0.20)
        assert probs.max_probability == 0.55
    
    def test_prediction_to_dict(self, sample_prediction):
        """Test Prediction serialization."""
        data = sample_prediction.to_dict()
        
        assert data["match_id"] == "match-001"
        assert data["over25_probability"] == 0.72
        assert "prediction_date" in data
        assert "result_probabilities" in data
    
    def test_prediction_from_dict(self, sample_prediction):
        """Test Prediction deserialization."""
        data = sample_prediction.to_dict()
        pred2 = Prediction.from_dict(data)
        
        assert pred2.match_id == sample_prediction.match_id
        assert pred2.over25_probability == sample_prediction.over25_probability
    
    def test_prediction_repr(self, sample_prediction):
        """Test Prediction string representation."""
        repr_str = repr(sample_prediction)
        assert "match-001" in repr_str
        assert "pending" in repr_str


class TestHomeAwayStats:
    """Tests for HomeAwayStats entity."""
    
    def test_create_empty_stats(self):
        """Test creating empty stats."""
        stats = HomeAwayStats()
        
        assert stats.matches_played == 0
        assert stats.goals_scored_avg == 0.0
    
    def test_stats_with_data(self):
        """Test stats with match data."""
        stats = HomeAwayStats(
            matches_played=10,
            goals_scored=20,
            goals_conceded=10,
            wins=7,
            draws=2,
            losses=1,
        )
        
        assert stats.goals_scored_avg == 2.0
        assert stats.goals_conceded_avg == 1.0
        assert stats.win_rate == 0.7
    
    def test_clean_sheet_rate(self):
        """Test clean sheet rate calculation."""
        stats = HomeAwayStats(
            matches_played=10,
            clean_sheets=4,
        )
        
        assert stats.clean_sheet_rate == 0.4


class TestTeamStats:
    """Tests for TeamStats entity."""
    
    def test_total_matches(self):
        """Test total matches calculation."""
        home = HomeAwayStats(matches_played=10)
        away = HomeAwayStats(matches_played=8)
        stats = TeamStats(home=home, away=away)
        
        assert stats.total_matches == 18
    
    def test_combined_averages(self):
        """Test combined home/away averages."""
        home = HomeAwayStats(matches_played=10, goals_scored=20, over_25_count=6)
        away = HomeAwayStats(matches_played=10, goals_scored=10, over_25_count=4)
        stats = TeamStats(home=home, away=away)
        
        assert stats.goals_scored_avg == 1.5  # 30/20
        assert stats.over_25_rate == 0.5  # 10/20
