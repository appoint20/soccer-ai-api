"""
Pydantic schemas for API request/response models.
"""
from datetime import date, datetime
from typing import Any, Dict, List, Optional
from pydantic import BaseModel, Field


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


class MatchAnalysis(BaseModel):
    """Complete analysis for a single match."""
    match_id: Optional[str] = None
    home_team: str
    away_team: str
    date: str
    time: Optional[str] = None
    league: str
    over25: Over25Prediction
    btts: BTTSPrediction
    result: ResultPrediction


class AnalyzeMatchesResponse(BaseModel):
    """Response for /analyze/matches endpoint."""
    date: str
    total_matches: int
    matches: List[MatchAnalysis]
    generated_at: str


class TicketSelection(BaseModel):
    """Single selection in a ticket."""
    match: str = Field(..., description="Home vs Away")
    league: str
    time: Optional[str] = None
    market: str = Field(..., description="over25, btts, home_win, etc.")
    selection: str = Field(..., description="The pick (YES, NO, H, D, A)")
    probability: float
    confidence: str


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
