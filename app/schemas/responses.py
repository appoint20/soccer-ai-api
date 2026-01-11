"""
Pydantic schemas for API responses
"""
from typing import Generic, TypeVar, List, Optional, Dict, Any
from pydantic import BaseModel
from datetime import date

T = TypeVar('T')


class PaginatedResponse(BaseModel, Generic[T]):
    """Pagination envelope for all endpoints"""
    offset: int
    limit: int
    total: int
    items: List[T]


# === Leagues ===
class League(BaseModel):
    id: str
    name: str
    country: str
    flag: str
    teams_count: int


# === Match Analysis ===
class AnalyzeRequest(BaseModel):
    date: str
    league_id: Optional[str] = None


class TeamStats(BaseModel):
    played: int
    wins: int
    draws: int
    losses: int
    goals_for: int
    goals_against: int
    form: List[str]
    clean_sheets: int
    avg_goals_scored: float
    avg_goals_conceded: float


class Prediction(BaseModel):
    prediction: str
    confidence: float
    reasoning: str


class PoissonAnalysis(BaseModel):
    expected_home_goals: float
    expected_away_goals: float
    hdw_probabilities: Dict[str, float]
    reasoning: str


class MonteCarloAnalysis(BaseModel):
    simulations: int
    hdw_probabilities: Dict[str, float]
    avg_total_goals: float
    reasoning: str


class PatternAnalysis(BaseModel):
    pattern: str
    all_methods_agree: bool
    confidence_level: str
    expected_accuracy: float
    reasoning: str


class TrapDetector(BaseModel):
    is_trap: bool
    warning_level: str  # NONE, LOW, MEDIUM, HIGH
    flags: List[str]
    message: Optional[str]


class Recommendation(BaseModel):
    bet: str
    confidence: str
    stake: str


class MatchAnalysis(BaseModel):
    match_id: str
    home_team: str
    away_team: str
    date: str
    league: str
    team_stats: Dict[str, TeamStats]
    ml_predictions: Dict[str, Prediction]
    poisson_analysis: PoissonAnalysis
    monte_carlo_analysis: MonteCarloAnalysis
    pattern_analysis: PatternAnalysis
    trap_detector: TrapDetector
    chatgpt_analysis: str
    recommendation: Recommendation


# === Tickets ===
class TicketGame(BaseModel):
    match: str
    bet: str
    odds: float
    confidence: float
    pattern: str


class Ticket(BaseModel):
    ticket_id: int
    stake: float
    games: List[TicketGame]
    combined_odds: float
    potential_return: float
    avg_confidence: float
    analysis: str


class TicketSummary(BaseModel):
    total_stake: float
    potential_total_return: float
    tickets_generated: int
    max_tickets_per_fixture: int


# === Backtest ===
class GameResult(BaseModel):
    date: str
    match: str
    league: str
    prediction: str
    actual: str
    correct: bool
    confidence: float
    pattern: str
    odds: float
    message: str


class LeaguePerformance(BaseModel):
    league_id: str
    league_name: str
    matches: int
    accuracy: float
    roi: float
    best_market: str
    worst_market: str


class ROICalculation(BaseModel):
    total_tickets: int
    winning_tickets: int
    losing_tickets: int
    total_staked: float
    total_returns: float
    profit: float
    roi_percentage: float
    win_rate: float


class BacktestSummary(BaseModel):
    period: str
    total_matches: int
    overall_accuracy: float
    pattern_breakdown: Dict[str, Dict[str, Any]]
