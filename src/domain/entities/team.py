"""Team entity with statistics tracking."""
from dataclasses import dataclass, field, asdict
from typing import Optional


@dataclass
class HomeAwayStats:
    """
    Statistics for home or away performance.
    
    Attributes:
        matches_played: Number of matches played
        goals_scored: Total goals scored
        goals_conceded: Total goals conceded
        wins: Number of wins
        draws: Number of draws
        losses: Number of losses
        clean_sheets: Matches with no goals conceded
        failed_to_score: Matches without scoring
        over_25_count: Matches with over 2.5 goals
        btts_count: Matches where both teams scored
    """
    matches_played: int = 0
    goals_scored: int = 0
    goals_conceded: int = 0
    wins: int = 0
    draws: int = 0
    losses: int = 0
    clean_sheets: int = 0
    failed_to_score: int = 0
    over_25_count: int = 0
    btts_count: int = 0
    
    @property
    def goals_scored_avg(self) -> float:
        """Average goals scored per match."""
        if self.matches_played == 0:
            return 0.0
        return self.goals_scored / self.matches_played
    
    @property
    def goals_conceded_avg(self) -> float:
        """Average goals conceded per match."""
        if self.matches_played == 0:
            return 0.0
        return self.goals_conceded / self.matches_played
    
    @property
    def over_25_rate(self) -> float:
        """Rate of matches with over 2.5 goals."""
        if self.matches_played == 0:
            return 0.0
        return self.over_25_count / self.matches_played
    
    @property
    def btts_rate(self) -> float:
        """Rate of matches where both teams scored."""
        if self.matches_played == 0:
            return 0.0
        return self.btts_count / self.matches_played
    
    @property
    def win_rate(self) -> float:
        """Win rate."""
        if self.matches_played == 0:
            return 0.0
        return self.wins / self.matches_played
    
    @property
    def clean_sheet_rate(self) -> float:
        """Clean sheet rate."""
        if self.matches_played == 0:
            return 0.0
        return self.clean_sheets / self.matches_played
    
    def to_dict(self) -> dict:
        """Convert to dictionary."""
        return asdict(self)
    
    @classmethod
    def from_dict(cls, data: dict) -> "HomeAwayStats":
        """Create from dictionary."""
        return cls(**data)


@dataclass
class TeamStats:
    """
    Overall team statistics combining home and away.
    
    Attributes:
        home: Home performance statistics
        away: Away performance statistics
    """
    home: HomeAwayStats = field(default_factory=HomeAwayStats)
    away: HomeAwayStats = field(default_factory=HomeAwayStats)
    
    @property
    def total_matches(self) -> int:
        """Total matches played."""
        return self.home.matches_played + self.away.matches_played
    
    @property
    def goals_scored_avg(self) -> float:
        """Overall average goals scored."""
        total_matches = self.total_matches
        if total_matches == 0:
            return 0.0
        total_goals = self.home.goals_scored + self.away.goals_scored
        return total_goals / total_matches
    
    @property
    def goals_conceded_avg(self) -> float:
        """Overall average goals conceded."""
        total_matches = self.total_matches
        if total_matches == 0:
            return 0.0
        total_goals = self.home.goals_conceded + self.away.goals_conceded
        return total_goals / total_matches
    
    @property
    def over_25_rate(self) -> float:
        """Overall over 2.5 goals rate."""
        total_matches = self.total_matches
        if total_matches == 0:
            return 0.0
        total_over = self.home.over_25_count + self.away.over_25_count
        return total_over / total_matches
    
    @property
    def btts_rate(self) -> float:
        """Overall BTTS rate."""
        total_matches = self.total_matches
        if total_matches == 0:
            return 0.0
        total_btts = self.home.btts_count + self.away.btts_count
        return total_btts / total_matches
    
    def to_dict(self) -> dict:
        """Convert to dictionary."""
        return {
            "home": self.home.to_dict(),
            "away": self.away.to_dict(),
        }
    
    @classmethod
    def from_dict(cls, data: dict) -> "TeamStats":
        """Create from dictionary."""
        return cls(
            home=HomeAwayStats.from_dict(data.get("home", {})),
            away=HomeAwayStats.from_dict(data.get("away", {})),
        )


@dataclass
class Team:
    """
    Represents a football team with all statistics.
    
    Attributes:
        name: Team name (standardized)
        league: Primary league code
        stats: Team statistics
        last_5_results: List of last 5 match results ('W', 'D', 'L')
        current_position: Current league position
    """
    name: str
    league: str
    stats: TeamStats = field(default_factory=TeamStats)
    last_5_results: list[str] = field(default_factory=list)
    current_position: Optional[int] = None
    
    @property
    def form_points(self) -> int:
        """Calculate form points from last 5 matches (W=3, D=1, L=0)."""
        points = 0
        for result in self.last_5_results[-5:]:
            if result == "W":
                points += 3
            elif result == "D":
                points += 1
        return points
    
    @property
    def form_string(self) -> str:
        """Get form as string (e.g., 'WWDLW')."""
        return "".join(self.last_5_results[-5:])
    
    def add_result(self, result: str) -> None:
        """Add a match result to the team's record."""
        if result in ("W", "D", "L"):
            self.last_5_results.append(result)
            # Keep only last 10 for memory efficiency
            if len(self.last_5_results) > 10:
                self.last_5_results = self.last_5_results[-10:]
    
    def to_dict(self) -> dict:
        """Convert to dictionary."""
        return {
            "name": self.name,
            "league": self.league,
            "stats": self.stats.to_dict(),
            "last_5_results": self.last_5_results,
            "current_position": self.current_position,
        }
    
    @classmethod
    def from_dict(cls, data: dict) -> "Team":
        """Create from dictionary."""
        return cls(
            name=data["name"],
            league=data["league"],
            stats=TeamStats.from_dict(data.get("stats", {})),
            last_5_results=data.get("last_5_results", []),
            current_position=data.get("current_position"),
        )
    
    def __repr__(self) -> str:
        """String representation."""
        return f"Team({self.name}, {self.league}, Form: {self.form_string})"
