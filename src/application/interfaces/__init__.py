"""Application interfaces (ports)."""
from src.application.interfaces.repositories import (
    IUpcomingMatchRepository,
    IHistoricalMatchRepository,
)

__all__ = ["IUpcomingMatchRepository", "IHistoricalMatchRepository"]
