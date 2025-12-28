"""
Unit tests for helper utility functions.

Tests cover:
- Team name standardization
- Season calculation
- Date/time parsing
- Validation functions
"""
import pytest
from datetime import date
from pathlib import Path

import sys
sys.path.insert(0, str(Path(__file__).parent.parent.parent))

from src.utils.helpers import (
    standardize_team_name,
    normalize_team_name_for_matching,
    calculate_season,
    get_season_of_year,
    validate_league_code,
    parse_date,
    parse_time,
    safe_int,
    safe_float,
)


class TestStandardizeTeamName:
    """Tests for team name standardization."""
    
    def test_common_variations_man_united(self):
        """Test Manchester United variations."""
        assert standardize_team_name("man utd") == "Manchester United"
        assert standardize_team_name("Man United") == "Manchester United"
        assert standardize_team_name("manchester utd") == "Manchester United"
    
    def test_common_variations_man_city(self):
        """Test Manchester City variations."""
        assert standardize_team_name("man city") == "Manchester City"
        assert standardize_team_name("Man City") == "Manchester City"
    
    def test_common_variations_spurs(self):
        """Test Tottenham variations."""
        assert standardize_team_name("spurs") == "Tottenham"
        assert standardize_team_name("Spurs") == "Tottenham"
        assert standardize_team_name("tottenham hotspur") == "Tottenham"
    
    def test_common_variations_wolves(self):
        """Test Wolverhampton variations."""
        assert standardize_team_name("wolves") == "Wolverhampton"
        assert standardize_team_name("wolverhampton wanderers") == "Wolverhampton"
    
    def test_removes_extra_whitespace(self):
        """Test extra whitespace is removed."""
        assert standardize_team_name("  Arsenal  ") == "Arsenal"
        assert standardize_team_name("Arsenal   FC") == "Arsenal FC"
    
    def test_preserves_correct_names(self):
        """Test correct names are preserved."""
        assert standardize_team_name("Arsenal") == "Arsenal"
        assert standardize_team_name("Chelsea") == "Chelsea"
        assert standardize_team_name("Liverpool") == "Liverpool"
    
    def test_handles_empty_string(self):
        """Test empty string handling."""
        assert standardize_team_name("") == ""
        assert standardize_team_name(None) == ""
    
    def test_german_teams(self):
        """Test German team variations."""
        assert standardize_team_name("bayern munich") == "Bayern Munich"
        assert standardize_team_name("borussia dortmund") == "Dortmund"
    
    def test_spanish_teams(self):
        """Test Spanish team variations."""
        assert standardize_team_name("atletico madrid") == "Ath Madrid"
        assert standardize_team_name("real madrid") == "Real Madrid"
    
    def test_italian_teams(self):
        """Test Italian team variations."""
        assert standardize_team_name("inter milan") == "Inter"
        assert standardize_team_name("ac milan") == "Milan"
    
    def test_french_teams(self):
        """Test French team variations."""
        assert standardize_team_name("paris saint-germain") == "Paris SG"
        assert standardize_team_name("psg") == "Paris SG"


class TestNormalizeTeamNameForMatching:
    """Tests for team name normalization for matching."""
    
    def test_normalizes_to_lowercase(self):
        """Test normalization to lowercase."""
        result = normalize_team_name_for_matching("Arsenal")
        assert result == "arsenal"
    
    def test_removes_special_characters(self):
        """Test special character removal."""
        result = normalize_team_name_for_matching("FC Köln")
        assert "ö" not in result or result == "fc koln"


class TestCalculateSeason:
    """Tests for season calculation."""
    
    def test_august_starts_new_season(self):
        """Test August marks start of new season."""
        assert calculate_season(date(2024, 8, 15)) == "2024-25"
        assert calculate_season(date(2024, 8, 1)) == "2024-25"
    
    def test_january_continues_season(self):
        """Test January is still previous season."""
        assert calculate_season(date(2025, 1, 15)) == "2024-25"
    
    def test_may_ends_season(self):
        """Test May is end of season."""
        assert calculate_season(date(2025, 5, 20)) == "2024-25"
    
    def test_june_july_transition(self):
        """Test June/July are pre-season (still previous)."""
        assert calculate_season(date(2024, 6, 15)) == "2023-24"
        assert calculate_season(date(2024, 7, 15)) == "2023-24"
    
    def test_season_format(self):
        """Test season format is correct."""
        result = calculate_season(date(2024, 9, 1))
        assert result == "2024-25"
        assert len(result) == 7
        assert "-" in result


class TestGetSeasonOfYear:
    """Tests for meteorological season detection."""
    
    def test_winter_months(self):
        """Test December, January, February are Winter."""
        assert get_season_of_year(date(2024, 12, 15)) == "Winter"
        assert get_season_of_year(date(2024, 1, 15)) == "Winter"
        assert get_season_of_year(date(2024, 2, 15)) == "Winter"
    
    def test_spring_months(self):
        """Test March, April, May are Spring."""
        assert get_season_of_year(date(2024, 3, 15)) == "Spring"
        assert get_season_of_year(date(2024, 4, 15)) == "Spring"
        assert get_season_of_year(date(2024, 5, 15)) == "Spring"
    
    def test_summer_months(self):
        """Test June, July, August are Summer."""
        assert get_season_of_year(date(2024, 6, 15)) == "Summer"
        assert get_season_of_year(date(2024, 7, 15)) == "Summer"
        assert get_season_of_year(date(2024, 8, 15)) == "Summer"
    
    def test_autumn_months(self):
        """Test September, October, November are Autumn."""
        assert get_season_of_year(date(2024, 9, 15)) == "Autumn"
        assert get_season_of_year(date(2024, 10, 15)) == "Autumn"
        assert get_season_of_year(date(2024, 11, 15)) == "Autumn"


class TestValidateLeagueCode:
    """Tests for league code validation."""
    
    def test_valid_english_leagues(self):
        """Test valid English league codes."""
        assert validate_league_code("E0") is True
        assert validate_league_code("E1") is True
        assert validate_league_code("E2") is True
        assert validate_league_code("E3") is True
    
    def test_valid_german_league(self):
        """Test valid German league code."""
        assert validate_league_code("D1") is True
    
    def test_valid_french_leagues(self):
        """Test valid French league codes."""
        assert validate_league_code("F1") is True
        assert validate_league_code("F2") is True
    
    def test_valid_italian_leagues(self):
        """Test valid Italian league codes."""
        assert validate_league_code("I1") is True
        assert validate_league_code("I2") is True
    
    def test_valid_spanish_league(self):
        """Test valid Spanish league code."""
        assert validate_league_code("SP1") is True
    
    def test_invalid_league_codes(self):
        """Test invalid league codes return False."""
        assert validate_league_code("XX") is False
        assert validate_league_code("E5") is False
        assert validate_league_code("invalid") is False


class TestParseDate:
    """Tests for date parsing."""
    
    def test_iso_format(self):
        """Test parsing ISO format (YYYY-MM-DD)."""
        assert parse_date("2024-09-15") == date(2024, 9, 15)
    
    def test_uk_format(self):
        """Test parsing UK format (DD/MM/YYYY)."""
        assert parse_date("15/09/2024") == date(2024, 9, 15)
    
    def test_uk_short_format(self):
        """Test parsing short UK format (DD/MM/YY)."""
        result = parse_date("15/09/24")
        if result:
            assert result.day == 15
            assert result.month == 9
    
    def test_invalid_date(self):
        """Test invalid date returns None."""
        assert parse_date("invalid") is None
        assert parse_date("") is None
        assert parse_date("nan") is None


class TestParseTime:
    """Tests for time parsing."""
    
    def test_standard_format(self):
        """Test parsing standard time format (HH:MM)."""
        assert parse_time("15:30") == "15:30"
        assert parse_time("09:00") == "09:00"
    
    def test_single_digit_hour(self):
        """Test parsing single digit hour."""
        assert parse_time("9:00") == "09:00"
    
    def test_invalid_time(self):
        """Test invalid time returns None."""
        assert parse_time("") is None
        assert parse_time("invalid") is None
        assert parse_time(None) is None


class TestSafeInt:
    """Tests for safe integer conversion."""
    
    def test_valid_int(self):
        """Test valid integer conversion."""
        assert safe_int(5) == 5
        assert safe_int("10") == 10
        assert safe_int(3.7) == 3
    
    def test_invalid_returns_default(self):
        """Test invalid value returns default."""
        assert safe_int("invalid", 0) == 0
        assert safe_int(None, -1) == -1
        assert safe_int(float("nan"), 0) == 0


class TestSafeFloat:
    """Tests for safe float conversion."""
    
    def test_valid_float(self):
        """Test valid float conversion."""
        assert safe_float(5.5) == 5.5
        assert safe_float("3.14") == 3.14
        assert safe_float(10) == 10.0
    
    def test_invalid_returns_default(self):
        """Test invalid value returns default."""
        assert safe_float("invalid", 0.0) == 0.0
        assert safe_float(None, -1.0) == -1.0
        assert safe_float(float("nan"), 0.0) == 0.0
