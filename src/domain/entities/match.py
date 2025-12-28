"""Match entity representing a football match."""
from dataclasses import dataclass, field, asdict
from datetime import date, time
from typing import Optional
import uuid


@dataclass
class Match:
    """
    Represents a football match with all relevant statistics.
    
    Attributes:
        id: Unique identifier for the match
        match_date: Date of the match
        match_time: Time of the match (can be None if not available)
        league: League code (e.g., 'E0' for Premier League)
        season: Season identifier (e.g., '2024-25')
        home_team: Home team name
        away_team: Away team name
        
        Full-time results:
        fthg: Full-time home goals
        ftag: Full-time away goals
        ftr: Full-time result ('H', 'D', 'A')
        
        Half-time results:
        hthg: Half-time home goals
        htag: Half-time away goals
        htr: Half-time result ('H', 'D', 'A')
        
        Match statistics:
        hs: Home shots
        as_: Away shots (as_ to avoid Python keyword conflict)
        hst: Home shots on target
        ast: Away shots on target
        hf: Home fouls
        af: Away fouls
        hc: Home corners
        ac: Away corners
        hy: Home yellow cards
        ay: Away yellow cards
        hr: Home red cards
        ar: Away red cards
        
        Other:
        referee: Match referee name
        home_position: Home team league position
        away_position: Away team league position
        
        Betting odds (optional):
        b365h: Bet365 home odds
        b365d: Bet365 draw odds
        b365a: Bet365 away odds
    """
    
    # Core identifiers
    home_team: str
    away_team: str
    match_date: date
    league: str
    season: str
    
    # Optional identifier
    id: str = field(default_factory=lambda: str(uuid.uuid4()))
    match_time: Optional[time] = None
    
    # Full-time results (None for upcoming matches)
    fthg: Optional[int] = None
    ftag: Optional[int] = None
    ftr: Optional[str] = None
    
    # Half-time results
    hthg: Optional[int] = None
    htag: Optional[int] = None
    htr: Optional[str] = None
    
    # Match statistics
    hs: Optional[int] = None   # Home shots
    as_: Optional[int] = None  # Away shots
    hst: Optional[int] = None  # Home shots on target
    ast: Optional[int] = None  # Away shots on target
    hf: Optional[int] = None   # Home fouls
    af: Optional[int] = None   # Away fouls
    hc: Optional[int] = None   # Home corners
    ac: Optional[int] = None   # Away corners
    hy: Optional[int] = None   # Home yellow cards
    ay: Optional[int] = None   # Away yellow cards
    hr: Optional[int] = None   # Home red cards
    ar: Optional[int] = None   # Away red cards
    
    # Other info
    referee: Optional[str] = None
    home_position: Optional[int] = None
    away_position: Optional[int] = None
    
    # Betting odds - 1X2
    b365h: Optional[float] = None
    b365d: Optional[float] = None
    b365a: Optional[float] = None
    
    # Betting odds - Over/Under 2.5 goals
    b365_over25: Optional[float] = None
    b365_under25: Optional[float] = None
    
    @property
    def total_goals(self) -> Optional[int]:
        """Calculate total goals in the match."""
        if self.fthg is not None and self.ftag is not None:
            return self.fthg + self.ftag
        return None
    
    @property
    def is_over_25(self) -> Optional[bool]:
        """Check if match had over 2.5 goals."""
        total = self.total_goals
        if total is not None:
            return total > 2.5
        return None
    
    @property
    def is_btts(self) -> Optional[bool]:
        """Check if both teams scored."""
        if self.fthg is not None and self.ftag is not None:
            return self.fthg > 0 and self.ftag > 0
        return None
    
    @property
    def is_completed(self) -> bool:
        """Check if match has been played."""
        return self.ftr is not None
    
    @property
    def match_key(self) -> str:
        """Generate a unique key for this match."""
        date_str = self.match_date.isoformat()
        return f"{date_str}_{self.home_team}_vs_{self.away_team}_{self.league}"
    
    def to_dict(self) -> dict:
        """Convert match to dictionary for JSON serialization."""
        data = asdict(self)
        # Convert date and time to strings
        data["match_date"] = self.match_date.isoformat()
        if self.match_time:
            data["match_time"] = self.match_time.strftime("%H:%M")
        else:
            data["match_time"] = None
        return data
    
    @classmethod
    def from_dict(cls, data: dict) -> "Match":
        """Create Match from dictionary."""
        # Parse date
        if isinstance(data.get("match_date"), str):
            data["match_date"] = date.fromisoformat(data["match_date"])
        
        # Parse time
        match_time = data.get("match_time")
        if match_time and isinstance(match_time, str):
            parts = match_time.split(":")
            data["match_time"] = time(int(parts[0]), int(parts[1]))
        
        return cls(**data)
    
    def __repr__(self) -> str:
        """String representation of match."""
        if self.is_completed:
            return (
                f"Match({self.home_team} {self.fthg}-{self.ftag} {self.away_team}, "
                f"{self.match_date}, {self.league})"
            )
        return (
            f"Match({self.home_team} vs {self.away_team}, "
            f"{self.match_date}, {self.league})"
        )
