from typing import List, Dict, Any, Optional
from datetime import datetime
from src.utils.logger import get_logger
from src.api.schemas import Ticket, TicketSelection
from src.domain.services.gemini_service import GeminiService

class TicketService:
    """
    Service for generating betting tickets.
    Encapsulates logic for filtering, grouping, and selecting matches.
    """
    
    def __init__(self, gemini_service: Optional[GeminiService] = None):
        self.logger = get_logger("TicketService")
        self.gemini_service = gemini_service or GeminiService()
        
    def generate_tickets_ai(self, predictions: List[Dict[str, Any]], prompt: str) -> Dict[str, Any]:
        """
        Generate tickets using Gemini AI.
        """
        if not predictions:
            return {"error": "No predictions provided"}
            
        self.logger.info(f"Generating AI tickets for {len(predictions)} matches")
        
        # Call Gemini
        result = self.gemini_service.generate_tickets(prompt, predictions)
        
        return result
        
    def generate_tickets(
        self, 
        predictions: List[Dict[str, Any]], 
        min_odds: float = 1.30, # valid params but we prioritize EV logic
        max_odds: float = 5.00,
        min_confidence: str = "MEDIUM" 
    ) -> List[Ticket]:
        """
        Generate tickets satisfying USER RULES:
        1. 3 matches per ticket.
        2. Max 2 games per league per ticket.
        3. No duplicate matches across tickets.
        4. Mixed markets.
        """
        if not predictions:
            return []

        # 1. Gather Candidates (One BEST market per match)
        candidates = []
        for p in predictions:
            # STRICTER QUALIFICATION: EV > 1.05 and Conf >= 0.40 (Allow high odds/lower conf)
            m = self._find_best_value_market(p, min_ev=1.05, min_conf=0.40)
            if m:
                candidates.append(m)
        
        # Sort by Quality (EV)
        candidates.sort(key=lambda x: x.odds * x.confidence, reverse=True)
        
        tickets = []
        ticket_id = 1
        used_match_ids = set()
        
        # 2. Build Tickets (Greedy Approach)
        # We iterate through candidates to start a ticket
        i = 0
        while i < len(candidates):
            current = candidates[i]
            
            # Skip if used
            if current.match_id in used_match_ids:
                i += 1
                continue
                
            # Start new ticket
            current_ticket_selections = [current]
            league_counts = {current.league: 1}
            
            # Try to complete ticket with next available candidates
            j = i + 1
            while j < len(candidates) and len(current_ticket_selections) < 3:
                candidate = candidates[j]
                
                # Checks:
                # 1. Not used
                if candidate.match_id in used_match_ids:
                    j += 1
                    continue
                # 2. Not same match
                if candidate.match_id == current.match_id:
                     j += 1
                     continue
                     
                # 3. League Constraint (Max 2)
                l_count = league_counts.get(candidate.league, 0)
                if l_count >= 2:
                    j += 1
                    continue
                    
                # Add to ticket
                current_ticket_selections.append(candidate)
                league_counts[candidate.league] = l_count + 1
                j += 1
            
            # Strictly 3 Matches per Ticket
            if len(current_ticket_selections) == 3:
                tickets.append(self._create_ticket(ticket_id, "accumulator_mixed", current_ticket_selections))
                ticket_id += 1
                # Mark as used matches
                for s in current_ticket_selections:
                    used_match_ids.add(s.match_id)
            
            # Move to next
            i += 1
            
        return tickets

    # generate_weekly_tickets removed

    def _find_best_market(
        self,
        p: Dict[str, Any],
        min_conf: float,
        min_odds: float = 1.01,
        max_odds: float = 10.0
    ) -> Optional[TicketSelection]:
        """Helper to find the best market statsifying criteria."""
        odds = p.get("odds", {})
        analysis = p.get("analysis", {})
        
        candidates = []
        
        # Helper to safely check and add
        def check(market_key, prob, conf, odd_key):
            odd = float(odds.get(odd_key) or 0.0)
            if odd <= 1.0: return # Invalid odds
            if min_odds <= odd <= max_odds and conf >= min_conf:
                 candidates.append((market_key, odd, prob or 0.0, conf or 0.0))

        # Over 2.5
        o25 = analysis.get("over25", {})
        if o25.get("prediction") == "YES":
             # Use probability as confidence proxy
             prob = float(o25.get("probability", 0.0))
             check("over25", prob, prob, "over25")

        # BTTS (Likely no odds, but logic remains valid if odds appear)
        btts = analysis.get("btts", {})
        if btts.get("prediction") == "YES":
             prob = float(btts.get("probability", 0.0))
             check("btts", prob, prob, "btts")
             
        # Result
        res = analysis.get("result", {})
        pred = res.get("prediction")
        res_prob = float(res.get("probability", 0.0))
        
        if pred == "H":
             check("home_win", res_prob, res_prob, "home")
        elif pred == "A":
             check("away_win", res_prob, res_prob, "away")
             
        if not candidates:
            return None
            
        # Select highest confidence
        best = max(candidates, key=lambda x: x[3])
        
        return TicketSelection(
            match_id=p.get("match_id"),
            home_team=p.get("home_team"),
            away_team=p.get("away_team"),
            league=p.get("league"),
            date=p.get("date"),
            market=best[0],
            odds=best[1],
            confidence=best[3],
            qualified=True
        )

    def _find_best_value_market(
        self,
        p: Dict[str, Any],
        min_ev: float,
        min_conf: float = 0.50
    ) -> Optional[TicketSelection]:
        """Helper to find the best market based on Expected Value (Odds * Confidence)."""
        odds = p.get("odds", {})
        analysis = p.get("analysis", {})
        
        candidates = []
        
        # Helper to safely check and add (checks EV)
        def check_ev(market_key, prob, conf, odd_key):
            odd = float(odds.get(odd_key) or 0.0)
            if odd <= 1.0: return # Invalid odds
            
            # EV Calculation
            ev = odd * conf
            if ev > min_ev and conf >= min_conf: # Ensure valid EV and > min_conf
                 candidates.append((market_key, odd, prob or 0.0, conf or 0.0, ev))

        # Over 2.5
        o25 = analysis.get("over25", {})
        if o25.get("prediction") == "YES":
             prob = float(o25.get("probability", 0.0))
             check_ev("over25", prob, prob, "over25")

        # BTTS
        btts = analysis.get("btts", {})
        if btts.get("prediction") == "YES":
             prob = float(btts.get("probability", 0.0))
             check_ev("btts", prob, prob, "btts")
             
        # Result
        res = analysis.get("result", {})
        pred = res.get("prediction")
        res_prob = float(res.get("probability", 0.0))
        
        if pred == "H":
             check_ev("home_win", res_prob, res_prob, "home")
        elif pred == "A":
             check_ev("away_win", res_prob, res_prob, "away")
             
        if not candidates:
            return None
            
        # Select highest EV
        best = max(candidates, key=lambda x: x[4])
        
        return TicketSelection(
            match_id=p.get("match_id"),
            home_team=p.get("home_team"),
            away_team=p.get("away_team"),
            league=p.get("league"),
            date=p.get("date"),
            market=best[0],
            odds=best[1],
            confidence=best[3],
            qualified=True
        )

    # _select_best_market removed/replaced by _find_best_market logic within loops
    
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
            combined_probability=round(combined_prob, 4),
            expected_value=round(combined_prob * total_odds, 4),
            risk_level="MEDIUM"
        )
