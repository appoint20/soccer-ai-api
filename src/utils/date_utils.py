"""
Date utility functions for feature engineering.

Provides helpers for date manipulation, season calculation,
and time-based analysis.
"""
from datetime import date, datetime, timedelta
from typing import Optional, Union


def get_season_from_date(dt: Union[date, datetime, str]) -> str:
    """
    Convert date to football season format (e.g., '2024-25').
    
    Football seasons run August to May:
    - Aug 2024 - May 2025 = '2024-25'
    - June/July are considered part of the previous season (pre-season)
    
    Args:
        dt: Date object or string in ISO format
        
    Returns:
        Season string (e.g., '2024-25')
    """
    if isinstance(dt, str):
        dt = parse_date_string(dt)
    if isinstance(dt, datetime):
        dt = dt.date()
    
    if dt is None:
        return "Unknown"
    
    year = dt.year
    month = dt.month
    
    # August onwards = new season starts
    if month >= 8:
        start_year = year
    else:
        # Jan-July = still previous season
        start_year = year - 1
    
    end_year = start_year + 1
    return f"{start_year}-{str(end_year)[-2:]}"


def get_season_of_year(dt: Union[date, datetime, str]) -> str:
    """
    Get meteorological season from date.
    
    Args:
        dt: Date object or string
        
    Returns:
        'Winter', 'Spring', 'Summer', or 'Autumn'
    """
    if isinstance(dt, str):
        dt = parse_date_string(dt)
    if isinstance(dt, datetime):
        dt = dt.date()
    
    if dt is None:
        return "Unknown"
    
    month = dt.month
    
    if month in [12, 1, 2]:
        return "Winter"
    elif month in [3, 4, 5]:
        return "Spring"
    elif month in [6, 7, 8]:
        return "Summer"
    else:  # 9, 10, 11
        return "Autumn"


def days_between_matches(date1: Union[date, str], date2: Union[date, str]) -> int:
    """
    Calculate days between two match dates.
    
    Useful for calculating rest periods.
    
    Args:
        date1: First match date
        date2: Second match date
        
    Returns:
        Absolute number of days between dates
    """
    if isinstance(date1, str):
        date1 = parse_date_string(date1)
    if isinstance(date2, str):
        date2 = parse_date_string(date2)
    
    if date1 is None or date2 is None:
        return 0
    
    if isinstance(date1, datetime):
        date1 = date1.date()
    if isinstance(date2, datetime):
        date2 = date2.date()
    
    return abs((date2 - date1).days)


def is_festive_period(dt: Union[date, datetime, str]) -> bool:
    """
    Check if date is in congested festive fixture period.
    
    December 20 - January 5 is typically the busiest period
    with 3 matches per week.
    
    Args:
        dt: Date to check
        
    Returns:
        True if in festive period
    """
    if isinstance(dt, str):
        dt = parse_date_string(dt)
    if isinstance(dt, datetime):
        dt = dt.date()
    
    if dt is None:
        return False
    
    month = dt.month
    day = dt.day
    
    # Dec 20-31 or Jan 1-5
    if month == 12 and day >= 20:
        return True
    if month == 1 and day <= 5:
        return True
    
    return False


def is_weekend(dt: Union[date, datetime, str]) -> bool:
    """
    Check if date is a weekend (Saturday or Sunday).
    
    Args:
        dt: Date to check
        
    Returns:
        True if weekend
    """
    if isinstance(dt, str):
        dt = parse_date_string(dt)
    if isinstance(dt, datetime):
        dt = dt.date()
    
    if dt is None:
        return False
    
    # weekday(): 0=Monday, 5=Saturday, 6=Sunday
    return dt.weekday() >= 5


def get_day_of_week(dt: Union[date, datetime, str]) -> str:
    """
    Get day of week name.
    
    Args:
        dt: Date
        
    Returns:
        Day name (e.g., 'Saturday')
    """
    if isinstance(dt, str):
        dt = parse_date_string(dt)
    if isinstance(dt, datetime):
        dt = dt.date()
    
    if dt is None:
        return "Unknown"
    
    days = ["Monday", "Tuesday", "Wednesday", "Thursday", 
            "Friday", "Saturday", "Sunday"]
    return days[dt.weekday()]


def get_month_name(dt: Union[date, datetime, str]) -> str:
    """
    Get month name from date.
    
    Args:
        dt: Date
        
    Returns:
        Month name (e.g., 'December')
    """
    if isinstance(dt, str):
        dt = parse_date_string(dt)
    if isinstance(dt, datetime):
        dt = dt.date()
    
    if dt is None:
        return "Unknown"
    
    months = ["January", "February", "March", "April", "May", "June",
              "July", "August", "September", "October", "November", "December"]
    return months[dt.month - 1]


def parse_date_string(date_str: str) -> Optional[date]:
    """
    Parse date string in various formats.
    
    Supports:
    - ISO format: 2024-12-29
    - UK format: 29/12/2024
    - Short UK: 29/12/24
    
    Args:
        date_str: Date string
        
    Returns:
        date object or None if parsing fails
    """
    if not date_str or date_str in ["nan", "NaT", ""]:
        return None
    
    date_str = str(date_str).strip()
    
    # Try ISO format first
    if "-" in date_str:
        try:
            return datetime.strptime(date_str[:10], "%Y-%m-%d").date()
        except ValueError:
            pass
    
    # Try UK formats
    if "/" in date_str:
        for fmt in ["%d/%m/%Y", "%d/%m/%y"]:
            try:
                return datetime.strptime(date_str, fmt).date()
            except ValueError:
                continue
    
    return None


def filter_matches_before_date(
    matches: list,
    cutoff_date: Union[date, str],
    date_field: str = "match_date"
) -> list:
    """
    Filter matches to only include those before a cutoff date.
    
    Critical for time-travel / preventing data leakage.
    
    Args:
        matches: List of match dicts or objects
        cutoff_date: Only include matches before this date
        date_field: Name of date field in matches
        
    Returns:
        Filtered list of matches
    """
    if isinstance(cutoff_date, str):
        cutoff_date = parse_date_string(cutoff_date)
    
    if cutoff_date is None:
        return matches
    
    filtered = []
    for match in matches:
        # Handle both dict and object
        if isinstance(match, dict):
            match_date = match.get(date_field)
        else:
            match_date = getattr(match, date_field, None)
        
        if isinstance(match_date, str):
            match_date = parse_date_string(match_date)
        
        if match_date and match_date < cutoff_date:
            filtered.append(match)
    
    return filtered


def get_matchweek_from_date(
    match_date: Union[date, str],
    season_start: Optional[date] = None
) -> int:
    """
    Estimate matchweek number from date.
    
    Rough estimate: ~1 matchweek per week from season start.
    
    Args:
        match_date: Date of match
        season_start: Start of season (defaults to Aug 10)
        
    Returns:
        Estimated matchweek number (1-38)
    """
    if isinstance(match_date, str):
        match_date = parse_date_string(match_date)
    
    if match_date is None:
        return 0
    
    if season_start is None:
        season = get_season_from_date(match_date)
        start_year = int(season.split("-")[0])
        season_start = date(start_year, 8, 10)  # Aug 10
    
    days_since_start = (match_date - season_start).days
    matchweek = max(1, min(38, (days_since_start // 7) + 1))
    
    return matchweek
