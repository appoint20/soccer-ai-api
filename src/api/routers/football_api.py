"""
Football API Router.

Endpoints for fetching fixtures and odds from API-Football.
"""
import os
from typing import Optional

from fastapi import APIRouter, Query, HTTPException, Depends

from src.models.football_schemas import (
    LeagueEnum,
    FixturesResponse,
    APIUsageStats,
    ErrorResponse,
)
from src.clients.api_football_client import APIFootballClient
from src.core.rate_limiter import get_rate_limiter, RateLimitExceeded
from src.utils.logger import get_logger

logger = get_logger("FootballAPIRouter")

router = APIRouter(prefix="/api/v1", tags=["Football API"])


# Client singleton
_client: Optional[APIFootballClient] = None


def get_api_client() -> APIFootballClient:
    """Get or create API-Football client."""
    global _client
    if _client is None:
        api_key = os.getenv("API_FOOTBALL_KEY", "")
        if not api_key:
            raise HTTPException(
                status_code=500,
                detail="API_FOOTBALL_KEY environment variable not set"
            )
        _client = APIFootballClient(api_key)
    return _client


@router.get(
    "/fixtures/upcoming/{league}",
    response_model=FixturesResponse,
    responses={
        400: {"model": ErrorResponse, "description": "Invalid league"},
        429: {"model": ErrorResponse, "description": "Rate limit exceeded"},
    },
    summary="Get upcoming fixtures with Bet365 odds",
    description="Fetch upcoming fixtures for a league with Bet365 betting odds.",
)
async def get_upcoming_fixtures(
    league: LeagueEnum,
    days_ahead: int = Query(default=7, ge=1, le=30, description="Days to look ahead"),
    client: APIFootballClient = Depends(get_api_client),
):
    """
    Get upcoming fixtures for a league.
    
    - **league**: League identifier (e.g., premier_league, la_liga)
    - **days_ahead**: Number of days to look ahead (1-30, default 7)
    
    Returns fixtures with Bet365 odds (Match Winner, Over/Under 2.5, BTTS).
    """
    try:
        result = await client.get_fixtures(league.value, days_ahead)
        return FixturesResponse(**result)
    
    except RateLimitExceeded as e:
        raise HTTPException(
            status_code=429,
            detail={
                "error": "Rate limit exceeded",
                "current_calls": e.current,
                "max_calls": e.max_calls,
                "remaining": e.remaining,
            }
        )
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))
    except Exception as e:
        logger.error(f"Failed to fetch fixtures: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@router.get(
    "/stats/api-usage",
    response_model=APIUsageStats,
    summary="Get API usage statistics",
    description="View current API-Football usage for today.",
)
async def get_api_usage():
    """
    Get API usage statistics.
    
    Returns:
    - **total_calls_today**: Number of API calls made today
    - **remaining_calls**: Calls remaining before limit
    - **max_daily_calls**: Maximum allowed calls per day (100)
    - **usage_percentage**: Percentage of daily limit used
    """
    rate_limiter = get_rate_limiter()
    stats = await rate_limiter.get_usage_stats()
    return APIUsageStats(**stats)


@router.get(
    "/leagues",
    summary="List supported leagues",
    description="Get list of all supported leagues with their IDs.",
)
async def list_leagues():
    """List all supported leagues."""
    return {
        "leagues": [
            {"key": league.value, "name": league.value.replace("_", " ").title()}
            for league in LeagueEnum
        ],
        "league_ids": APIFootballClient.LEAGUES,
    }
