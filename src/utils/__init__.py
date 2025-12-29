"""Utility functions for the soccer prediction API."""
from src.utils.config import Config, get_config
from src.utils.logger import get_logger, setup_logging
from src.utils.helpers import (
    standardize_team_name,
    normalize_team_name_for_matching,
    calculate_season,
    parse_date,
    parse_time,
    validate_league_code,
    safe_int,
    safe_float,
)
from src.utils.date_utils import (
    get_season_from_date,
    get_season_of_year,
    days_between_matches,
    is_festive_period,
    filter_matches_before_date,
)
from src.utils.stats_utils import (
    calculate_exponential_weights,
    weighted_average,
    calculate_rate,
    detect_trend,
    normalize_value,
)

__all__ = [
    "Config",
    "get_config",
    "get_logger",
    "setup_logging",
    "standardize_team_name",
    "normalize_team_name_for_matching",
    "calculate_season",
    "parse_date",
    "parse_time",
    "validate_league_code",
]
