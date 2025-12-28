"""Helper utility functions."""
import re
from datetime import date, datetime
from typing import Optional

from src.utils.config import get_config


# Team name mapping for common variations
TEAM_NAME_MAPPINGS = {
    # Premier League
    "man united": "Manchester United",
    "man utd": "Manchester United",
    "manchester utd": "Manchester United",
    "man city": "Manchester City",
    "manchester city": "Manchester City",
    "spurs": "Tottenham",
    "tottenham hotspur": "Tottenham",
    "wolves": "Wolverhampton",
    "wolverhampton wanderers": "Wolverhampton",
    "brighton & hove albion": "Brighton",
    "brighton and hove albion": "Brighton",
    "west ham united": "West Ham",
    "newcastle united": "Newcastle",
    "nottingham forest": "Nott'm Forest",
    "nott'm forest": "Nott'm Forest",
    "nottm forest": "Nott'm Forest",
    "sheffield utd": "Sheffield United",
    "sheffield united": "Sheffield United",
    "crystal palace": "Crystal Palace",
    "aston villa": "Aston Villa",
    
    # Championship
    "west brom": "West Bromwich",
    "west bromwich albion": "West Bromwich",
    "birmingham city": "Birmingham",
    "blackburn rovers": "Blackburn",
    "bristol city": "Bristol City",
    "cardiff city": "Cardiff",
    "coventry city": "Coventry",
    "hull city": "Hull",
    "ipswich town": "Ipswich",
    "leeds united": "Leeds",
    "leicester city": "Leicester",
    "middlesbrough": "Middlesbrough",
    "millwall": "Millwall",
    "norwich city": "Norwich",
    "plymouth argyle": "Plymouth",
    "preston north end": "Preston",
    "queens park rangers": "QPR",
    "qpr": "QPR",
    "rotherham united": "Rotherham",
    "sheffield wednesday": "Sheffield Weds",
    "stoke city": "Stoke",
    "sunderland": "Sunderland",
    "swansea city": "Swansea",
    "watford": "Watford",
    
    # German teams
    "bayern munich": "Bayern Munich",
    "bayern munchen": "Bayern Munich",
    "fc bayern": "Bayern Munich",
    "borussia dortmund": "Dortmund",
    "borussia m'gladbach": "M'gladbach",
    "borussia monchengladbach": "M'gladbach",
    "bayer leverkusen": "Leverkusen",
    "rb leipzig": "RB Leipzig",
    "rasenballsport leipzig": "RB Leipzig",
    "eintracht frankfurt": "Ein Frankfurt",
    "sc freiburg": "Freiburg",
    "vfb stuttgart": "Stuttgart",
    "vfl wolfsburg": "Wolfsburg",
    "werder bremen": "Werder Bremen",
    "fc koln": "FC Koln",
    "1. fc koln": "FC Koln",
    "fc augsburg": "Augsburg",
    "tsg hoffenheim": "Hoffenheim",
    "1899 hoffenheim": "Hoffenheim",
    "mainz 05": "Mainz",
    "1. fsv mainz 05": "Mainz",
    "vfl bochum": "Bochum",
    "union berlin": "Union Berlin",
    "1. fc union berlin": "Union Berlin",
    "hertha berlin": "Hertha",
    "hertha bsc": "Hertha",
    
    # Spanish teams
    "atletico madrid": "Ath Madrid",
    "atletico de madrid": "Ath Madrid",
    "real madrid": "Real Madrid",
    "fc barcelona": "Barcelona",
    "real sociedad": "Sociedad",
    "athletic bilbao": "Ath Bilbao",
    "athletic club": "Ath Bilbao",
    "real betis": "Betis",
    "sevilla fc": "Sevilla",
    "villarreal cf": "Villarreal",
    "valencia cf": "Valencia",
    "celta vigo": "Celta",
    "real celta de vigo": "Celta",
    "getafe cf": "Getafe",
    "rcd espanyol": "Espanol",
    "espanyol": "Espanol",
    "deportivo alaves": "Alaves",
    "cd leganes": "Leganes",
    "rcd mallorca": "Mallorca",
    "ca osasuna": "Osasuna",
    "rayo vallecano": "Vallecano",
    "ud las palmas": "Las Palmas",
    "real valladolid": "Valladolid",
    
    # French teams
    "paris saint-germain": "Paris SG",
    "paris sg": "Paris SG",
    "psg": "Paris SG",
    "olympique marseille": "Marseille",
    "olympique lyon": "Lyon",
    "olympique lyonnais": "Lyon",
    "as monaco": "Monaco",
    "stade rennais": "Rennes",
    "rc lens": "Lens",
    "ogc nice": "Nice",
    "losc lille": "Lille",
    "fc nantes": "Nantes",
    "montpellier hsc": "Montpellier",
    "stade brestois": "Brest",
    "stade de reims": "Reims",
    "racing strasbourg": "Strasbourg",
    "rc strasbourg": "Strasbourg",
    "toulouse fc": "Toulouse",
    "fc lorient": "Lorient",
    "clermont foot": "Clermont",
    "le havre ac": "Le Havre",
    "fc metz": "Metz",
    
    # Italian teams
    "inter milan": "Inter",
    "fc internazionale": "Inter",
    "internazionale": "Inter",
    "ac milan": "Milan",
    "juventus fc": "Juventus",
    "as roma": "Roma",
    "ssc napoli": "Napoli",
    "atalanta bc": "Atalanta",
    "ss lazio": "Lazio",
    "acf fiorentina": "Fiorentina",
    "bologna fc": "Bologna",
    "torino fc": "Torino",
    "us sassuolo": "Sassuolo",
    "udinese calcio": "Udinese",
    "hellas verona": "Verona",
    "empoli fc": "Empoli",
    "us lecce": "Lecce",
    "us salernitana": "Salernitana",
    "spezia calcio": "Spezia",
    "cagliari calcio": "Cagliari",
    "genoa cfc": "Genoa",
    "frosinone calcio": "Frosinone",
}


def standardize_team_name(name: str) -> str:
    """
    Standardize team name to handle variations.
    
    Args:
        name: Raw team name
        
    Returns:
        Standardized team name
    """
    if not name:
        return ""
    
    # Clean the name
    clean_name = name.strip()
    
    # Check for known mappings (case-insensitive)
    lower_name = clean_name.lower()
    if lower_name in TEAM_NAME_MAPPINGS:
        return TEAM_NAME_MAPPINGS[lower_name]
    
    # Apply general cleaning
    # Remove extra whitespace
    clean_name = re.sub(r'\s+', ' ', clean_name)
    
    # Title case
    clean_name = clean_name.title()
    
    # Handle common suffixes that should stay uppercase
    clean_name = re.sub(r'\bFc\b', 'FC', clean_name)
    clean_name = re.sub(r'\bAfc\b', 'AFC', clean_name)
    clean_name = re.sub(r'\bUtd\b', 'United', clean_name)
    
    return clean_name


def normalize_team_name_for_matching(name: str) -> str:
    """
    Create a normalized version of team name for matching purposes.
    
    Args:
        name: Team name
        
    Returns:
        Lowercase, stripped version for matching
    """
    if not name:
        return ""
    
    # First standardize
    standardized = standardize_team_name(name)
    
    # Then normalize for matching
    normalized = standardized.lower()
    normalized = re.sub(r'[^a-z0-9\s]', '', normalized)
    normalized = re.sub(r'\s+', ' ', normalized).strip()
    
    return normalized


def calculate_season(match_date: date) -> str:
    """
    Calculate the season for a given date.
    
    Seasons run from August to May, so:
    - Aug 2024 - May 2025 = "2024-25"
    
    Args:
        match_date: Date of the match
        
    Returns:
        Season string (e.g., "2024-25")
    """
    year = match_date.year
    month = match_date.month
    
    # If before August, it's previous season
    if month < 8:
        start_year = year - 1
    else:
        start_year = year
    
    end_year = start_year + 1
    
    return f"{start_year}-{str(end_year)[-2:]}"


def get_season_of_year(match_date: date) -> str:
    """
    Get the meteorological season for a date.
    
    Args:
        match_date: Date to check
        
    Returns:
        Season name ('Winter', 'Spring', 'Summer', 'Autumn')
    """
    month = match_date.month
    
    if month in (12, 1, 2):
        return "Winter"
    elif month in (3, 4, 5):
        return "Spring"
    elif month in (6, 7, 8):
        return "Summer"
    else:
        return "Autumn"


def validate_league_code(code: str) -> bool:
    """
    Check if a league code is supported.
    
    Args:
        code: League code to validate
        
    Returns:
        True if league is supported
    """
    config = get_config()
    supported = config.get_supported_leagues()
    
    # If no leagues configured, accept common codes
    if not supported:
        common_codes = [
            "E0", "E1", "E2", "E3",  # England
            "D1", "D2",              # Germany
            "F1", "F2",              # France
            "I1", "I2",              # Italy
            "SP1", "SP2",            # Spain
        ]
        return code in common_codes
    
    return code in supported


def parse_date(date_str: str) -> Optional[date]:
    """
    Parse a date string in various formats.
    
    Args:
        date_str: Date string to parse
        
    Returns:
        Parsed date or None if invalid
    """
    if not date_str or date_str == "nan":
        return None
    
    # Common date formats
    formats = [
        "%Y-%m-%d",      # ISO format
        "%d/%m/%Y",      # UK format
        "%d/%m/%y",      # Short UK format
        "%d-%m-%Y",      # Dash UK format
        "%m/%d/%Y",      # US format
        "%Y/%m/%d",      # Alternative ISO
    ]
    
    for fmt in formats:
        try:
            return datetime.strptime(str(date_str), fmt).date()
        except ValueError:
            continue
    
    return None


def parse_time(time_str: str) -> Optional[str]:
    """
    Parse a time string and return standardized format.
    
    Args:
        time_str: Time string to parse
        
    Returns:
        Time in HH:MM format or None
    """
    if not time_str or str(time_str) in ("nan", "None", ""):
        return None
    
    time_str = str(time_str).strip()
    
    # Try HH:MM format
    match = re.match(r'^(\d{1,2}):(\d{2})$', time_str)
    if match:
        hours = int(match.group(1))
        minutes = int(match.group(2))
        return f"{hours:02d}:{minutes:02d}"
    
    # Try HHMM format
    match = re.match(r'^(\d{2})(\d{2})$', time_str)
    if match:
        hours = int(match.group(1))
        minutes = int(match.group(2))
        return f"{hours:02d}:{minutes:02d}"
    
    return None


def safe_int(value, default: int = 0) -> int:
    """
    Safely convert a value to int.
    
    Args:
        value: Value to convert
        default: Default value if conversion fails
        
    Returns:
        Integer value or default
    """
    try:
        if value is None or (isinstance(value, float) and str(value) == 'nan'):
            return default
        return int(float(value))
    except (ValueError, TypeError):
        return default


def safe_float(value, default: float = 0.0) -> float:
    """
    Safely convert a value to float.
    
    Args:
        value: Value to convert
        default: Default value if conversion fails
        
    Returns:
        Float value or default
    """
    try:
        if value is None or (isinstance(value, float) and str(value) == 'nan'):
            return default
        return float(value)
    except (ValueError, TypeError):
        return default
