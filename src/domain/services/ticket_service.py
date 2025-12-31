from typing import List, Dict, Any, Optional
from datetime import datetime

from src.utils.logger import get_logger
from src.api.schemas import Ticket, TicketSelection, WeeklyTicket

class TicketService:
    """
    Service for generating betting tickets.
    Encapsulates logic for filtering, grouping, and selecting matches.
    """
    
    def __init__(self):
        self.logger = get_logger("TicketService")
        
    def generate_tickets(
        self, 
        predictions: List[Dict[str, Any]], 
        min_odds: float = 1.60,
        max_odds: float = 2.80,
        min_confidence: str = "MEDIUM"
    ) -> List[Ticket]:
        """
        Generate daily tickets based on predictions and constraints.
        """
        # Group by league
        by_league = {}
        for p in predictions:
            league = p.get("league")
            if league not in by_league:
                by_league[league] = []
            by_league[league].append(p)
            
        tickets = []
        ticket_id = 1
        
        # 1. Accumulators (One per league, max 2 matches)
        for league, preds in by_league.items():
            selections = []
            
            # Sort by highest probability logic (simplified for now)
            # You might want to sort by value/confidence here
            
            for p in preds:
                # Find best market for this match
                best_market = self._select_best_market(p, min_odds, max_odds, min_confidence)
                if best_market:
                    selections.append(best_market)
            
            # Take top 2
            if len(selections) >= 2:
                top_selections = selections[:2]
                tickets.append(self._create_ticket(ticket_id, "accumulator", top_selections))
                ticket_id += 1
                
        # 2. Singles (High confidence)
        # ... logic as needed ...
        
        return tickets

    def _select_best_market(
        self, 
        prediction: Dict[str, Any], 
        min_odds: float, 
        max_odds: float,
        min_confidence: str
    ) -> Optional[TicketSelection]:
        """Select the best market for a match prediction."""
        
        match_info = {
            "match_id": prediction.get("match_id", f"{prediction.get('home_team')}-{prediction.get('away_team')}"),
            "home_team": prediction.get("home_team"),
            "away_team": prediction.get("away_team"),
            "league": prediction.get("league"),
            "date": prediction.get("date"),
        }
        
        # Check Over 2.5
        o25 = prediction.get("over25", {})
        if (o25.get("prediction") == "YES" and 
            self._check_confidence(o25.get("confidence"), min_confidence)):
            
            # Check odds if available (mocking check as odds are nested)
            # In real flow, odds should be passed in prediction object
            # Assuming prediction object has odd property mapped
            
            return TicketSelection(
                **match_info,
                market="over25",
                odds=1.80, # Placeholder/Logic needs odds from prediction
                confidence=o25.get("probability", 0.0),
                qualified=p.get("team_stats", {}).get("qualification", {}).get("over25_qualified", False)
            )
            
        # ... Other markets ...
        return None

    def _check_confidence(self, actual: str, minimum: str) -> bool:
        """Check confidence level."""
        levels = {"LOW": 1, "MEDIUM": 2, "HIGH": 3}
        return levels.get(actual, 0) >= levels.get(minimum, 0)

    def _create_ticket(self, id: int, type: str, selections: List[TicketSelection]) -> Ticket:
        """Create a ticket object."""
        combined_prob = 1.0
        total_odds = 1.0
        for s in selections:
            combined_prob *= s.confidence
            total_odds *= s.odds
            
        return Ticket(
            ticket_id=str(id),
            ticket_type=type,
            selections=selections,
            combined_probability=combined_prob,
            risk_level="MEDIUM" # Simple logic
        )
