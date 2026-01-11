"""
Pydantic schemas for API request/response models.
"""
from datetime import date, datetime
from typing import Any, Dict, List, Optional
from enum import Enum
from dataclasses import dataclass
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


# ============== Analysis Schemas ==============

class TeamFormStats(BaseModel):
    """Detailed form statistics for a team for a specific lookback window."""
    over_25_rate: float = Field(0.0, ge=0, le=1)
    btts_rate: float = Field(0.0, ge=0, le=1)
    win_rate: float = Field(0.0, ge=0, le=1)
    lose_rate: float = Field(0.0, ge=0, le=1)
    draw_rate: float = Field(0.0, ge=0, le=1)
    goals_2_3_rate: float = Field(0.0, ge=0, le=1)
    avg_goals_scored: float = Field(0.0, ge=0)
    avg_goals_conceded: float = Field(0.0, ge=0)
    form: Optional[str] = None


class H2HStats(BaseModel):
    """H2H statistics with reliability score ."""
    over_25_rate: float = Field(0.0, ge=0, le=1)
    btts_rate: float = Field(0.0, ge=0, le=1)
    home_win_rate: float = Field(0.0, ge=0, le=1)
    away_win_rate: float = Field(0.0, ge=0, le=1)
    draw_rate: float = Field(0.0, ge=0, le=1)
    goals_2_3_rate: float = Field(0.0, ge=0, le=1)
    avg_total_goals: float = Field(0.0, ge=0)
    total_matches: int = Field(0, ge=0)
    h2h_reliability: float = Field(0.0, ge=0, le=1)


class PoissonProbabilities(BaseModel):
    """Poisson + Dixon-Coles probabilities ."""
    expected_score: str = Field("", description="Formatted score e.g. '1.25 - 0.85'")
    expected_home_goals: float = Field(0.0, ge=0)
    expected_away_goals: float = Field(0.0, ge=0)
    home_win: float = Field(0.0, ge=0, le=1)
    draw: float = Field(0.0, ge=0, le=1)
    away_win: float = Field(0.0, ge=0, le=1)
    over_25: float = Field(0.0, ge=0, le=1)
    under_25: float = Field(0.0, ge=0, le=1)
    btts: float = Field(0.0, ge=0, le=1)
    btts_no: float = Field(0.0, ge=0, le=1)
    goals_2_3: float = Field(0.0, ge=0, le=1)


class MonteCarloMarketResult(BaseModel):
    """Monte Carlo result for a single market ."""
    adjusted_probability: float = Field(0.0, ge=0, le=1)
    confidence_lower: float = Field(0.0, ge=0, le=1)
    confidence_upper: float = Field(0.0, ge=0, le=1)
    streak_length: int = Field(0, ge=0)
    regression_applied: bool = False


class MonteCarloResults(BaseModel):
    """Monte Carlo results - flat probability format."""
    over_25_probability: float = Field(0.0, ge=0, le=1)
    btts_probability: float = Field(0.0, ge=0, le=1)
    home_win_probability: float = Field(0.0, ge=0, le=1)
    away_win_probability: float = Field(0.0, ge=0, le=1)
    draw_probability: float = Field(0.0, ge=0, le=1)
    goals_2_3_probability: float = Field(0.0, ge=0, le=1)


class AIAnalysis(BaseModel):
    """AI-generated analysis for a match."""
    best_prediction: str = Field(..., description="Recommended bet: Over 2.5, BTTS Yes, Home Win, etc.")
    reason: str = Field(..., description="2-3 sentences explaining why")
    short_analysis: str = Field(..., description="3-5 sentences match outlook")
    confidence_level: str = Field(..., description="HIGH, MEDIUM, or LOW")
    trap: Optional[str] = Field(None, description="Warning about potential traps")


class VenueFormStats(BaseModel):
    """Venue-specific form statistics (home or away)."""
    form_string: str = ""
    matches_played: int = 0
    wins: int = 0
    draws: int = 0
    losses: int = 0
    goals_scored: int = 0
    goals_conceded: int = 0
    points: int = 0
    avg_goals_scored: float = 0.0
    avg_goals_conceded: float = 0.0
    over_25_rate: float = 0.0
    btts_rate: float = 0.0
    win_rate: float = 0.0
    lose_rate: float = 0.0
    draw_rate: float = 0.0
    goals_2_3_rate: float = 0.0


class MatchAnalysisMarket(BaseModel):
    """Single market analysis result."""
    probability: float = 0.0
    probability_pct: str = "0%"
    confidence: str = "LOW"
    qualified: bool
    qualification_reason: Optional[str] = None


class DrawAnalysis(BaseModel):
    """Draw analysis result (Draw Gravity Score)."""
    probability: float = 0.0
    probability_pct: str = "0%"
    confidence: str = "LOW"
    qualified: bool
    reason: Optional[str] = None
    draw_gravity_score: int = Field(..., ge=0, le=100)


class MatchAnalysisResult(BaseModel):
    """Aggregated match analysis for all markets."""
    over_25: MatchAnalysisMarket
    btts: MatchAnalysisMarket
    goals_2_3: MatchAnalysisMarket
    home_win: MatchAnalysisMarket
    away_win: MatchAnalysisMarket
    draw: DrawAnalysis
    confidence_index: int = 0
    classic_draw_profile: Optional["ClassicDrawProfile"] = None


@dataclass
class TeamStats(BaseModel):
    """Canonical team statistics."""
    last_5: Optional['TeamFormStats'] = None
    venue_last_3: Optional['TeamFormStats'] = None
    position: Optional[int] = None
    points: Optional[int] = None
    goals_scored: Optional[int] = None
    goals_conceded: Optional[int] = None

class MatchAnalysis(BaseModel):
    """Canonical match analysis object (Single Source of Truth)."""
    match_id: str
    home_team: str
    away_team: str
    date: str
    time: Optional[str] = None
    league: str
    
    # Enrichment Flattened
    matchday: int = 0
    position_difference: int = 0
    points_difference: int = 0
    
    # Canonical Stats Objects
    homeStats: TeamStats
    awayStats: TeamStats
    h2h_last_5: H2HStats
    
    # Probabilities & Confidence
    poisson: PoissonProbabilities
    monte_carlo: MonteCarloResults
    overall_confidence: float
    
    # Aggregated Result (renamed from aggregated_markets)
    match_analysis: Optional[MatchAnalysisResult] = None
    
    # AI & Odds
    ai_analysis: Optional[AIAnalysis] = None
    odds: Optional[dict] = None
    
    # Backtest
    backtest_result: Optional["BacktestResult"] = None


class ClassicDrawProfile(BaseModel):
    """Internal profile for structural draw detection."""
    classic_draw_score: int
    classic_draw_detected: bool
    reason: str


# ============== Aggregated Markets Schemas (Chart-Ready) ==============

class MarketSourceSchema(BaseModel):
    """Individual source contribution to a market."""
    source: str
    probability: float
    weight: float
    contribution: float


class AggregatedMarketSchema(BaseModel):
    """Aggregated probability for a single betting market."""
    market: str
    probability: float
    probability_pct: str
    confidence: str  # HIGH, MEDIUM, LOW
    qualified: bool
    sources: List[MarketSourceSchema]
    source_variance: float


class AggregatedMarketsSchema(BaseModel):
    """Complete aggregation result for all markets (chart-ready)."""
    over_25: AggregatedMarketSchema
    btts: AggregatedMarketSchema
    goals_2_3: AggregatedMarketSchema
    home_win: AggregatedMarketSchema
    away_win: AggregatedMarketSchema
    draw: AggregatedMarketSchema
    confidence_index: int
    radar_chart_data: Optional[Dict[str, float]] = None
    best_markets: Optional[List[str]] = None


class AnalyzeResponse(BaseModel):
    """Paginated response for analysis."""
    items: List[MatchAnalysis]
    total: int
    page: int
    limit: int
    generated_at: str
    # Backtest stats (only for past dates)
    is_past_date: bool = False
    backtest_stats: Optional["BacktestStats"] = None


class BacktestResult(BaseModel):
    """Result of a prediction vs actual outcome."""
    actual_score: str  # e.g. "2-1"
    actual_result: str  # "H", "D", "A"
    
    # AI Specific
    predicted_market: Optional[str] = None # e.g. "Over 2.5", "Home Win"
    was_correct: Optional[bool] = None
    explanation: Optional[str] = None  # Why prediction was wrong (if wrong)
    
    # Statistical Specific
    is_btts: Optional[bool] = None
    is_over25: Optional[bool] = None
    predictions: Optional[Dict[str, Any]] = None # Detailed breakdown by market


class BacktestStats(BaseModel):
    """Daily accuracy statistics."""
    total_predictions: int
    correct_predictions: int
    incorrect_predictions: int
    accuracy_percentage: float
    by_market: Dict[str, Dict[str, int]] = {}  # {"Over 2.5": {"correct": 5, "total": 8}}
