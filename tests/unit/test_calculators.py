"""
Unit tests for V2 Analysis Calculators.

Tests:
- TeamFormCalculator with recency weighting
- H2HStatsCalculator with reliability scoring
- PoissonGoalCalculator with Dixon-Coles
- MonteCarloUncertaintyAdjuster with capped regression
- MatchConfidenceCalculator aggregation
"""
import pytest
from datetime import date, timedelta

from src.domain.services.calculators.team_form_calculator import TeamFormCalculator, TeamFormStats
from src.domain.services.calculators.h2h_stats_calculator import H2HStatsCalculator, H2HStats
from src.domain.services.calculators.poisson_goal_calculator import PoissonGoalCalculator, PoissonProbabilities
from src.domain.services.calculators.monte_carlo_uncertainty_adjuster import (
    MonteCarloUncertaintyAdjuster,
    MonteCarloResult,
)
from src.domain.services.calculators.match_confidence_calculator import MatchConfidenceCalculator


# ============== Test Fixtures ==============

@pytest.fixture
def sample_matches():
    """Generate sample historical matches for testing."""
    base_date = date.today() - timedelta(days=30)
    matches = []
    
    for i in range(10):
        match_date = base_date - timedelta(days=i * 3)
        matches.append({
            "Date": match_date.isoformat(),
            "HomeTeam": "Team A" if i % 2 == 0 else "Team B",
            "AwayTeam": "Team B" if i % 2 == 0 else "Team A",
            "FTHG": 2 if i % 3 == 0 else 1,
            "FTAG": 1 if i % 2 == 0 else 0,
            "Div": "E0",
        })
    
    return matches


@pytest.fixture
def form_calculator():
    return TeamFormCalculator(default_decay=0.85)


@pytest.fixture
def h2h_calculator():
    return H2HStatsCalculator(max_age_seasons=3, min_reliable_matches=3)


@pytest.fixture
def poisson_calculator():
    return PoissonGoalCalculator()


@pytest.fixture
def mc_adjuster():
    return MonteCarloUncertaintyAdjuster(max_adjustment=0.07)


@pytest.fixture
def confidence_calculator():
    return MatchConfidenceCalculator()


# ============== TeamFormCalculator Tests ==============

class TestTeamFormCalculator:
    """Tests for TeamFormCalculator."""
    
    def test_calculate_form_stats_returns_stats(self, form_calculator, sample_matches):
        """Should return TeamFormStats with populated values."""
        stats = form_calculator.calculate_form_stats(
            team="Team A",
            matches=sample_matches,
            last_n=5,
        )
        
        assert isinstance(stats, TeamFormStats)
        assert stats.sample_size > 0
        assert stats.effective_sample_size > 0
        assert 0 <= stats.win_rate <= 1
        assert 0 <= stats.over_25_rate <= 1
    
    def test_decay_weighting_reduces_effective_sample(self, form_calculator, sample_matches):
        """Effective sample size should be less than raw sample size due to decay."""
        stats = form_calculator.calculate_form_stats(
            team="Team A",
            matches=sample_matches,
            last_n=5,
            decay=0.85,
        )
        
        # With decay=0.85, effective should be less than raw
        assert stats.effective_sample_size < stats.sample_size
    
    def test_venue_filter_home(self, form_calculator, sample_matches):
        """Should only include home matches when venue_filter='home'."""
        stats = form_calculator.calculate_form_stats(
            team="Team A",
            matches=sample_matches,
            last_n=5,
            venue_filter="home",
        )
        
        assert isinstance(stats, TeamFormStats)
        # Home matches should be filtered
        assert stats.sample_size <= 5
    
    def test_empty_matches_returns_empty_stats(self, form_calculator):
        """Should return empty stats for empty match list."""
        stats = form_calculator.calculate_form_stats(
            team="Team A",
            matches=[],
            last_n=5,
        )
        
        assert stats.sample_size == 0
        assert stats.effective_sample_size == 0
    
    def test_to_dict_conversion(self, form_calculator, sample_matches):
        """to_dict should return properly formatted dictionary."""
        stats = form_calculator.calculate_form_stats(
            team="Team A",
            matches=sample_matches,
            last_n=5,
        )
        
        result = stats.to_dict()
        assert "over_25_rate" in result
        assert "sample_size" in result
        assert "effective_sample_size" in result


# ============== H2HStatsCalculator Tests ==============

class TestH2HStatsCalculator:
    """Tests for H2HStatsCalculator."""
    
    def test_calculate_h2h_stats(self, h2h_calculator, sample_matches):
        """Should calculate H2H stats between two teams."""
        stats = h2h_calculator.calculate_h2h_stats(
            home_team="Team A",
            away_team="Team B",
            matches=sample_matches,
            last_n=5,
        )
        
        assert isinstance(stats, H2HStats)
        assert stats.total_matches > 0
        assert 0 <= stats.h2h_reliability <= 1
    
    def test_reliability_dampening_for_sparse_data(self, h2h_calculator):
        """Reliability should be dampened when < 3 H2H matches."""
        sparse_matches = [
            {"Date": "2025-01-01", "HomeTeam": "A", "AwayTeam": "B", "FTHG": 1, "FTAG": 0},
        ]
        
        stats = h2h_calculator.calculate_h2h_stats(
            home_team="A",
            away_team="B",
            matches=sparse_matches,
            last_n=5,
        )
        
        # With only 1 match, reliability should be low
        assert stats.h2h_reliability < 0.6
    
    def test_no_h2h_matches_returns_zero_reliability(self, h2h_calculator):
        """Should return 0 reliability when no H2H matches exist."""
        stats = h2h_calculator.calculate_h2h_stats(
            home_team="Team X",
            away_team="Team Y",
            matches=[],
            last_n=5,
        )
        
        assert stats.total_matches == 0
        assert stats.h2h_reliability == 0.0


# ============== PoissonGoalCalculator Tests ==============

class TestPoissonGoalCalculator:
    """Tests for PoissonGoalCalculator."""
    
    def test_calculate_probabilities(self, poisson_calculator):
        """Should calculate probabilities summing close to 1."""
        home_stats = TeamFormStats(
            over_25_rate=0.6,
            btts_rate=0.5,
            win_rate=0.5,
            draw_rate=0.2,
            lose_rate=0.3,
            goals_2_3_rate=0.4,
            avg_goals_scored=1.5,
            avg_goals_conceded=1.0,
            sample_size=5,
            effective_sample_size=4.0,
        )
        away_stats = TeamFormStats(
            over_25_rate=0.5,
            btts_rate=0.4,
            win_rate=0.4,
            draw_rate=0.3,
            lose_rate=0.3,
            goals_2_3_rate=0.35,
            avg_goals_scored=1.2,
            avg_goals_conceded=1.2,
            sample_size=5,
            effective_sample_size=4.0,
        )
        
        probs = poisson_calculator.calculate_probabilities(
            home_team="Team A",
            away_team="Team B",
            home_stats=home_stats,
            away_stats=away_stats,
            league_code="E0",
            league_avg_goals=2.7,
        )
        
        assert isinstance(probs, PoissonProbabilities)
        # 1X2 should sum to ~1
        total_1x2 = probs.home_win + probs.draw + probs.away_win
        assert 0.99 <= total_1x2 <= 1.01
        
        # Over/Under should be complementary
        assert abs(probs.over_25 + probs.under_25 - 1.0) < 0.01
    
    def test_expected_goals_calculated(self, poisson_calculator):
        """Should calculate expected goals for both teams."""
        home_stats = TeamFormStats(
            avg_goals_scored=2.0,
            avg_goals_conceded=1.0,
            sample_size=5,
            effective_sample_size=4.0,
        )
        away_stats = TeamFormStats(
            avg_goals_scored=1.0,
            avg_goals_conceded=2.0,
            sample_size=5,
            effective_sample_size=4.0,
        )
        
        probs = poisson_calculator.calculate_probabilities(
            home_team="A",
            away_team="B",
            home_stats=home_stats,
            away_stats=away_stats,
            league_code="E0",
        )
        
        assert probs.expected_home_goals > 0
        assert probs.expected_away_goals > 0


# ============== MonteCarloUncertaintyAdjuster Tests ==============

class TestMonteCarloUncertaintyAdjuster:
    """Tests for MonteCarloUncertaintyAdjuster."""
    
    def test_adjustment_capped_at_max(self, mc_adjuster):
        """Adjustment should never exceed MAX_ADJUSTMENT (±7%)."""
        # Extreme streak of 5 consecutive True outcomes
        result = mc_adjuster.adjust_probability(
            base_probability=0.5,
            recent_outcomes=[True, True, True, True, True],
            market_type="over_25",
        )
        
        deviation = abs(result.adjusted_probability - 0.5)
        assert deviation <= 0.07, f"Deviation {deviation} exceeds max 0.07"
    
    def test_no_adjustment_for_short_streaks(self, mc_adjuster):
        """No regression should be applied for streaks < 3."""
        result = mc_adjuster.adjust_probability(
            base_probability=0.5,
            recent_outcomes=[True, True],
            market_type="over_25",
        )
        
        assert result.regression_applied is False
    
    def test_streak_detection(self, mc_adjuster):
        """Should correctly detect streak length."""
        result = mc_adjuster.adjust_probability(
            base_probability=0.5,
            recent_outcomes=[True, True, True, False, True],
            market_type="over_25",
        )
        
        # Streak of 3 True at start
        assert result.streak_length == 3
    
    def test_empty_outcomes_returns_base_probability(self, mc_adjuster):
        """Should return base probability when no outcomes provided."""
        result = mc_adjuster.adjust_probability(
            base_probability=0.6,
            recent_outcomes=[],
            market_type="over_25",
        )
        
        assert result.adjusted_probability == 0.6
        assert result.regression_applied is False
    
    def test_calibration_deviation_within_bounds(self, mc_adjuster):
        """CRITICAL: MC should never deviate > 7% from Poisson."""
        test_cases = [
            (0.3, [True, True, True, True, True]),
            (0.7, [False, False, False, False, False]),
            (0.5, [True, False, True, False, True]),
        ]
        
        for base_prob, outcomes in test_cases:
            result = mc_adjuster.adjust_probability(base_prob, outcomes, "test")
            deviation = abs(result.adjusted_probability - base_prob)
            # Use small tolerance for floating point comparison
            assert deviation <= 0.07 + 1e-9, (
                f"Calibration failed: base={base_prob}, adjusted={result.adjusted_probability}"
            )


# ============== MatchConfidenceCalculator Tests ==============

class TestMatchConfidenceCalculator:
    """Tests for MatchConfidenceCalculator."""
    
    def test_confidence_in_valid_range(self, confidence_calculator):
        """Confidence should be between 0 and 100."""
        from src.domain.services.calculators.monte_carlo_uncertainty_adjuster import (
            MonteCarloResults,
            MonteCarloResult,
        )
        
        home_stats = TeamFormStats(sample_size=5, effective_sample_size=4.0)
        away_stats = TeamFormStats(sample_size=4, effective_sample_size=3.5)
        h2h_stats = H2HStats(total_matches=3, h2h_reliability=0.7)
        poisson_probs = PoissonProbabilities(
            home_win=0.45, draw=0.25, away_win=0.30,
            over_25=0.55, btts=0.50, goals_2_3=0.35,
        )
        mc_results = MonteCarloResults(
            over_25=MonteCarloResult(adjusted_probability=0.53),
            btts=MonteCarloResult(adjusted_probability=0.48),
            home_win=MonteCarloResult(adjusted_probability=0.43),
            away_win=MonteCarloResult(adjusted_probability=0.28),
            draw=MonteCarloResult(adjusted_probability=0.27),
            goals_2_3=MonteCarloResult(adjusted_probability=0.33),
        )
        
        confidence = confidence_calculator.calculate_confidence_index(
            home_stats=home_stats,
            away_stats=away_stats,
            h2h_stats=h2h_stats,
            poisson_probs=poisson_probs,
            mc_results=mc_results,
            league_code="E0",
        )
        
        assert 0 <= confidence <= 100
        assert isinstance(confidence, int)


# ============== Run Tests ==============

if __name__ == "__main__":
    pytest.main([__file__, "-v"])
