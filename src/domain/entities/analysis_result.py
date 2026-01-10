from dataclasses import dataclass, field
from typing import Dict, Any, Optional

@dataclass
class MatchAnalysisResult:
    """
    Detailed analysis result for a match.
    Domain entity representing the persistent state of an analysis.
    """
    match_id: str
    date: str
    league: str
    home_team: str
    away_team: str
    
    # Stored as dictionaries to decouple from complex sub-objects during persistence
    enrichment_data: Dict[str, Any]
    h2h_stats: Dict[str, Any]
    poisson: Dict[str, Any]
    monte_carlo: Dict[str, Any]
    aggregated_markets: Optional[Dict[str, Any]] = None
    ai_analysis: Optional[Dict[str, Any]] = None
    odds: Optional[Dict[str, Any]] = None
    overall_confidence: float = 0.0
    cached_at: Optional[str] = None
    
    def to_dict(self) -> Dict[str, Any]:
        data = {
            "match_id": self.match_id,
            "date": self.date,
            "league": self.league,
            "home_team": self.home_team,
            "away_team": self.away_team,
            "enrichment_data": self.enrichment_data,
            "h2h_stats": self.h2h_stats,
            "poisson": self.poisson,
            "monte_carlo": self.monte_carlo,
            "aggregated_markets": self.aggregated_markets,
            "ai_analysis": self.ai_analysis,
            "odds": self.odds,
            "overall_confidence": self.overall_confidence,
        }
        if self.cached_at:
            data["cached_at"] = self.cached_at
        return data
    
    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> 'MatchAnalysisResult':
        # Safely handle missing optional fields
        return cls(
            match_id=data["match_id"],
            date=data["date"],
            league=data.get("league", ""),
            home_team=data.get("home_team", ""),
            away_team=data.get("away_team", ""),
            enrichment_data=data.get("enrichment_data", {}),
            h2h_stats=data.get("h2h_stats", {}),
            poisson=data.get("poisson", {}),
            monte_carlo=data.get("monte_carlo", {}),
            aggregated_markets=data.get("aggregated_markets"),
            ai_analysis=data.get("ai_analysis"),
            odds=data.get("odds"),
            overall_confidence=data.get("overall_confidence", 0.0),
            cached_at=data.get("cached_at")
        )
