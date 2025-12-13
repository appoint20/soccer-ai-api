"""
Pydantic models for Enhanced Predictions endpoint
"""
from typing import Dict, List, Optional
from pydantic import BaseModel


class EnhancedPredictionsRequest(BaseModel):
    """Request model for enhanced predictions endpoint."""
    date: str  # YYYY-MM-DD format
    league_id: Optional[str] = None  # E.g., 'E0' for Premier League


class EnhancedPredictionsResponse(BaseModel):
    """Response model for enhanced predictions endpoint."""
    date: str
    leagues: Dict[str, dict]  # League folder -> league data with gemini analysis
    total_matches: int
    total_leagues: int
