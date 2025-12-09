"""
Tickets Generation API Route
"""
from fastapi import APIRouter, Query
from typing import List
from datetime import datetime

router = APIRouter()


@router.get("/tickets/generate")
async def generate_tickets(
    date: str = Query(..., description="Date for fixtures (YYYY-MM-DD)"),
    min_confidence: float = Query(0.65, ge=0.5, le=1.0),
    max_tickets: int = Query(4, ge=1, le=10),
    offset: int = Query(0, ge=0),
    limit: int = Query(4, ge=1, le=10)
):
    """Generate betting tickets with 3 games per ticket, confidence filter"""
    
    # Mock high-confidence games
    available_games = [
        {"match": "Man City vs Liverpool", "bet": "Home Win", "odds": 2.20, "confidence": 0.72, "pattern": "STRONG_CONSENSUS", "league": "E0"},
        {"match": "Arsenal vs Chelsea", "bet": "Home Win", "odds": 1.80, "confidence": 0.75, "pattern": "STRONG_CONSENSUS", "league": "E0"},
        {"match": "Bayern vs Dortmund", "bet": "Over 2.5", "odds": 1.65, "confidence": 0.81, "pattern": "STRONG_CONSENSUS", "league": "D1"},
        {"match": "Real Madrid vs Barcelona", "bet": "BTTS", "odds": 1.72, "confidence": 0.78, "pattern": "STRONG_CONSENSUS", "league": "SP1"},
        {"match": "PSG vs Lyon", "bet": "Home Win", "odds": 1.55, "confidence": 0.82, "pattern": "STRONG_CONSENSUS", "league": "F1"},
        {"match": "Inter vs AC Milan", "bet": "Over 1.5", "odds": 1.40, "confidence": 0.88, "pattern": "STRONG_CONSENSUS", "league": "I1"},
        {"match": "Leeds vs Sheffield", "bet": "Home Win", "odds": 2.10, "confidence": 0.68, "pattern": "PARTIAL_CONSENSUS", "league": "E1"},
        {"match": "Freiburg vs Mainz", "bet": "BTTS", "odds": 1.85, "confidence": 0.71, "pattern": "STRONG_CONSENSUS", "league": "D1"},
        {"match": "Napoli vs Roma", "bet": "Over 2.5", "odds": 1.90, "confidence": 0.69, "pattern": "STRONG_CONSENSUS", "league": "I1"},
        {"match": "Sevilla vs Valencia", "bet": "Draw", "odds": 3.40, "confidence": 0.66, "pattern": "PARTIAL_CONSENSUS", "league": "SP1"},
        {"match": "Monaco vs Nice", "bet": "Home Win", "odds": 1.95, "confidence": 0.70, "pattern": "STRONG_CONSENSUS", "league": "F1"},
        {"match": "Juventus vs Atalanta", "bet": "Draw", "odds": 3.20, "confidence": 0.67, "pattern": "PARTIAL_CONSENSUS", "league": "I1"},
    ]
    
    # Filter by confidence
    filtered_games = [g for g in available_games if g['confidence'] >= min_confidence]
    
    # Sort by confidence (highest first)
    filtered_games.sort(key=lambda x: x['confidence'], reverse=True)
    
    # Generate tickets (3 games each)
    tickets = []
    games_used = 0
    
    for i in range(min(max_tickets, len(filtered_games) // 3)):
        ticket_games = filtered_games[games_used:games_used + 3]
        if len(ticket_games) < 3:
            break
        
        combined_odds = 1.0
        for g in ticket_games:
            combined_odds *= g['odds']
        
        avg_conf = sum(g['confidence'] for g in ticket_games) / 3
        
        tickets.append({
            "ticket_id": i + 1,
            "stake": 100,
            "games": [
                {
                    "match": g['match'],
                    "bet": g['bet'],
                    "odds": g['odds'],
                    "confidence": g['confidence'],
                    "pattern": g['pattern']
                }
                for g in ticket_games
            ],
            "combined_odds": round(combined_odds, 2),
            "potential_return": round(100 * combined_odds, 2),
            "avg_confidence": round(avg_conf, 2),
            "expected_accuracy": 0.693 if all(g['pattern'] == 'STRONG_CONSENSUS' for g in ticket_games) else 0.5,
            "analysis": f"Ticket {i + 1} combines {len(ticket_games)} high-confidence picks with average {avg_conf:.1%} confidence."
        })
        
        games_used += 3
    
    # Summary
    total_stake = len(tickets) * 100
    total_potential = sum(t['potential_return'] for t in tickets)
    
    # Apply pagination
    total = len(tickets)
    items = tickets[offset:offset + limit]
    
    return {
        "offset": offset,
        "limit": limit,
        "total": total,
        "items": items,
        "summary": {
            "total_stake": total_stake,
            "potential_total_return": round(total_potential, 2),
            "tickets_generated": len(tickets),
            "max_tickets_per_fixture": max_tickets
        }
    }
