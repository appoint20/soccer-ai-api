"""
Pydantic schemas for API request/response models.
"""
from datetime import date, datetime
from typing import Any, Dict, List, Optional
from enum import Enum
from pydantic import BaseModel, Field, ConfigDict


class ModelTier(str, Enum):
    """Model tier enum."""
    TIER1 = "tier1"
    TIER2 = "tier2"
    TIER3 = "tier3"


# ============== Request Schemas ==============

class AnalyzeMatchesRequest(BaseModel):
    """Query params for match analysis (used as reference)."""
    date: str = Field(..., description="Date in YYYY-MM-DD format", pattern=r"^\d{4}-\d{2}-\d{2}$")


class GenerateTicketsRequest(BaseModel):
    """Query params for ticket generation."""
    date: str = Field(..., description="Date in YYYY-MM-DD format", pattern=r"^\d{4}-\d{2}-\d{2}$")
    min_confidence: str = Field(default="MEDIUM", description="Minimum confidence level (LOW, MEDIUM, HIGH)")


# ============== Response Schemas ==============

class Over25Prediction(BaseModel):
    """Over 2.5 goals prediction."""
    prediction: str = Field(..., description="YES or NO")
    probability: float = Field(..., ge=0, le=1, description="Probability (0-1)")
    confidence: str = Field(..., description="HIGH, MEDIUM, or LOW")


class BTTSPrediction(BaseModel):
    """Both teams to score prediction."""
    prediction: str = Field(..., description="YES or NO")
    probability: float = Field(..., ge=0, le=1, description="Probability (0-1)")
    confidence: str = Field(..., description="HIGH, MEDIUM, or LOW")


class ResultProbabilities(BaseModel):
    """Match result probabilities."""
    home_win: float = Field(..., ge=0, le=1)
    draw: float = Field(..., ge=0, le=1)
    away_win: float = Field(..., ge=0, le=1)


class ResultPrediction(BaseModel):
    """Match result prediction."""
    prediction: str = Field(..., description="H, D, or A")
    probabilities: ResultProbabilities
    confidence: str = Field(..., description="HIGH, MEDIUM, or LOW")


class TeamMatchStats(BaseModel):
    """Team statistics for BTTS/Over25 qualification."""
    overall_9: Dict[str, Any] = Field(..., description="Last 9 matches overall")
    venue_6: Dict[str, Any] = Field(..., description="Last 6 home/away matches")


class MatchTeamStats(BaseModel):
    """Statistics for both teams in a match."""
    btts: Dict[str, Any] = Field(..., description="BTTS statistics")
    over25: Dict[str, Any] = Field(..., description="Over 2.5 statistics")
    qualification: Dict[str, Any] = Field(..., description="Qualification flags")


class MatchOdds(BaseModel):
    """Match betting odds."""
    home: Optional[float] = Field(None, description="Home win odds")
    draw: Optional[float] = Field(None, description="Draw odds")
    away: Optional[float] = Field(None, description="Away win odds")
    over25: Optional[float] = Field(None, description="Over 2.5 goals odds")
    btts: Optional[float] = Field(None, description="Both teams to score odds")


class MatchAverage(BaseModel):
    """Average statistics for the match."""
    home_goal_avg: float = Field(0.0, description="Home team goals average")
    away_goal_avg: float = Field(0.0, description="Away team goals average")
    home_win_rate: float = Field(0.0, description="Home team win rate")
    away_win_rate: float = Field(0.0, description="Away team win rate")
    home_conceded_avg: float = Field(0.0, description="Home team conceded average")
    away_conceded_avg: float = Field(0.0, description="Away team conceded average")


class MatchH2H(BaseModel):
    """Head-to-head statistics."""
    total_matches: int = Field(0, description="Total H2H matches")
    home_wins: int = Field(0, description="Home team wins")
    draws: int = Field(0, description="Draws")
    away_wins: int = Field(0, description="Away team wins")
    avg_goals: float = Field(0.0, description="Average goals in H2H")
    btts_rate: float = Field(0.0, description="BTTS rate in H2H")
    over25_rate: float = Field(0.0, description="Over 2.5 rate in H2H")


class MLModelPrediction(BaseModel):
    """ML model prediction output."""
    prediction: str = Field(..., description="H, D, or A")
    confidence: float = Field(..., description="Confidence 0-1")
    over25: Dict[str, Any] = Field(..., description="Over 2.5 prediction")
    btts: Dict[str, Any] = Field(..., description="BTTS prediction")


class PoissonDistribution(BaseModel):
    """Poisson distribution probabilities."""
    home_win: float = Field(0.0, description="Home win probability")
    draw: float = Field(0.0, description="Draw probability")
    away_win: float = Field(0.0, description="Away win probability")
    over25: float = Field(0.0, description="Over 2.5 probability")
    btts: float = Field(0.0, description="BTTS probability")
    expected_home_goals: float = Field(0.0, description="Expected home goals")
    expected_away_goals: float = Field(0.0, description="Expected away goals")


class MatchAnalysis(BaseModel):
    """Complete analysis for a single match."""
    match_id: Optional[str] = None
    home_team: str
    away_team: str
    date: str
    time: Optional[str] = None
    league: str
    
    # Odds
    odds: Optional[MatchOdds] = Field(None, description="Betting odds")
    
    # Averages
    average: Optional[MatchAverage] = Field(None, description="Team averages")
    
    # H2H
    h2h: Optional[MatchH2H] = Field(None, description="Head-to-head stats")
    
    # Predictions (consolidated from ML model)
    predictions: Optional[Dict[str, Any]] = Field(None, description="All predictions (over25, btts, result)")
    
    # Poisson
    poisson_distribution: Optional[PoissonDistribution] = Field(None, description="Poisson probabilities")
    
    # Team Stats (BTTS/Over25 qualification)
    team_stats: Optional[MatchTeamStats] = Field(None, description="BTTS/Over25 team stats")

    # New fields
    is_derby: bool = Field(False, description="Is derby match")
    raw_predictions: Optional[Dict[str, Any]] = Field(None, description="Raw model outputs")
    ai_insight: Optional[Dict[str, Any]] = Field(None, description="Gemini AI generated insight")


class MarketStats(BaseModel):
    """Detailed stats for a specific market (Over25, BTTS, Result)."""
    home_last_3_home: float = 0.0
    away_last_3_away: float = 0.0
    home_last_5_overall: float = 0.0
    away_last_5_overall: float = 0.0
    h2h_last_5: float = 0.0
    poisson_probability: float = 0.0
    prediction: str = "N/A"
    probability: float = 0.0


class AggregateMatchAnalysis(BaseModel):
    """Refactored match analysis with aggregated stats."""
    match_id: Optional[str] = None
    home_team: str
    away_team: str
    date: str
    time: Optional[str] = None
    league: str
    odds: Optional[MatchOdds] = None
    
    analysis: Dict[str, MarketStats] # Keys: "over25", "btts", "result"
    ai_insight: Optional[Dict[str, Any]] = None


class MatchAnalysisDto(BaseModel):
    """Match analysis for API response (similar to MatchAnalysis but strictly typed)."""
    match_id: Optional[str]
    home_team: str
    away_team: str
    date: str
    time: Optional[str] = None
    league: str
    odds: Optional[MatchOdds]
    predictions: Optional[Dict[str, Any]]
    poisson_distribution: Optional[PoissonDistribution]
    team_stats: Optional[MatchTeamStats]
    h2h: Optional[MatchH2H]
    average: Optional[MatchAverage]
    ai_insight: Optional[Dict[str, Any]]


class AnalysisResponse(BaseModel):
    """Response for comprehensive analysis."""
    date: str
    total_matches: int
    matches: List[Dict[str, Any]] # Flexible dict to accommodate various analysis depths
    summary: str



class ComprehensiveMatchAnalysis(BaseModel):
    """Comprehensive analysis with all models and statistics."""
    match_info: Dict[str, Any] = Field(..., description="Match details")
    team_stats: Dict[str, Any] = Field(..., description="Home and away team stats")
    h2h: Dict[str, Any] = Field(..., description="Head-to-head statistics")
    predictions: Dict[str, Any] = Field(..., description="ML, Dixon-Coles, Monte Carlo, Ensemble")
    confidence_summary: Dict[str, str] = Field(..., description="Confidence levels")


class ComprehensiveAnalyzeResponse(BaseModel):
    """Response for comprehensive analysis endpoint."""
    date: str
    total_matches: int
    matches: List[ComprehensiveMatchAnalysis]
    generated_at: str


class AnalyzeMatchesResponse(BaseModel):
    """Response for /analyze/matches endpoint with pagination."""
    model_config = {"json_schema_extra": {"exclude_none": True}}
    
    items: List[AggregateMatchAnalysis] = Field(..., description="Array of match analyses")
    total: int = Field(..., description="Total number of matches")
    offset: Optional[int] = Field(None, description="Pagination offset (only if provided)")
    limit: Optional[int] = Field(None, description="Pagination limit (only if provided)")
    generated_at: str
    
    def model_dump(self, **kwargs):
        """Override to exclude None values."""
        kwargs.setdefault("exclude_none", True)
        return super().model_dump(**kwargs)


class TicketSelection(BaseModel):
    """Single selection in a ticket."""
    match_id: str
    home_team: str
    away_team: str
    league: str
    date: str
    market: str = Field(..., description="over25, btts, home_win, draw, away_win")
    odds: float
    confidence: float
    qualified: bool = False


class Ticket(BaseModel):
    """A betting ticket with multiple selections."""
    ticket_id: str
    ticket_type: str = Field(..., description="accumulator, single, etc.")
    selections: List[TicketSelection]
    combined_probability: float
    expected_value: Optional[float] = None
    risk_level: str = Field(..., description="LOW, MEDIUM, HIGH")


class GenerateTicketsResponse(BaseModel):
    """Response for /tickets/generate endpoint."""
    date: str
    tickets: List[Ticket]
    total_tickets: int
    offset: Optional[int] = None
    limit: Optional[int] = None
    generated_at: str


class ModelInfo(BaseModel):
    """Information about a trained model."""
    model_name: str
    model_type: str
    version: str
    is_trained: bool
    training_date: Optional[str] = None
    n_features: int


class ModelsInfoResponse(BaseModel):
    """Response for /models/info endpoint."""
    tier: str
    models_loaded: bool
    models: Dict[str, ModelInfo]


class HealthResponse(BaseModel):
    """Health check response."""
    status: str
    version: str
    timestamp: str


class ErrorResponse(BaseModel):
    """Error response."""
    error: str
    detail: Optional[str] = None
    timestamp: str


class MarketAccuracy(BaseModel):
    """Market accuracy details."""
    total_predictions: int
    qualified_predictions: int
    correct_predictions: int
    accuracy: float


class LeagueAccuracy(BaseModel):
    """League accuracy details."""
    league_code: str
    league_name: str
    total_matches: int
    qualified_matches: int
    ignored_matches: int
    accuracy: Dict[str, float] = Field(..., description="Accuracy per market")


class BacktestSummary(BaseModel):
    """Backtest summary."""
    test_period: Dict[str, Any]
    total_matches: int
    qualified_matches: int
    risky_matches: int
    derbies_excluded: int
    confidence_threshold: float


class ModelAgreementLevel(BaseModel):
    """Model agreement level accuracy."""
    total: int
    correct: int
    accuracy: float


class BacktestResponse(BaseModel):
    """Response for backtest endpoint."""
    summary: BacktestSummary
    market_accuracy: Dict[str, MarketAccuracy]
    model_agreement: Dict[str, Dict[str, ModelAgreementLevel]] = Field(
        ..., description="Accuracy when models agree/disagree"
    )
    league_accuracy: List[LeagueAccuracy]
    weekly_breakdown: List[Dict[str, Any]]
    generated_at: str


# ============== Backtest Report Schemas ==============

class LeagueQualificationStats(BaseModel):
    """Qualification stats for a single league."""
    league: str = Field(..., description="League code")
    total_matches: int
    qualified_matches: int
    not_qualified_matches: int
    qualified_pct: float = Field(..., description="Percentage qualified")
    over25_accuracy_pct: float = Field(0.0, description="Over 2.5 accuracy %")
    btts_accuracy_pct: float = Field(0.0, description="BTTS accuracy %")
    result_accuracy_pct: float = Field(0.0, description="Result accuracy %")


class MarketAccuracyReport(BaseModel):
    """Market accuracy with percentages."""
    total: int
    correct: int
    accuracy_pct: float = Field(..., description="Accuracy as percentage")
    qualified_total: int = 0
    qualified_correct: int = 0
    qualified_accuracy_pct: float = 0.0


class BacktestReportResponse(BaseModel):
    """Detailed backtest report response."""
    test_period: Dict[str, str]
    total_matches: int
    qualified_matches: int
    qualified_pct: float
    not_qualified_matches: int
    not_qualified_pct: float
    
    market_accuracy: Dict[str, MarketAccuracyReport]
    league_stats: List[LeagueQualificationStats]
    
    generated_at: str



