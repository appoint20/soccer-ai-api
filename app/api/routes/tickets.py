"""
Tickets Generation API Route - GEMINI AI POWERED
"""
from fastapi import APIRouter, Query
from typing import List
from datetime import datetime
import pandas as pd
from pathlib import Path

from app.api.routes.matches import load_fixtures, analyze_match, load_valid_leagues
from app.services.gemini_service import get_gemini_service

router = APIRouter()


@router.get("/tickets/generate")
async def generate_tickets(
    date: str = Query(..., description="Date for fixtures (YYYY-MM-DD)"),
    min_confidence: float = Query(0.65, ge=0.5, le=1.0),
    max_tickets: int = Query(4, ge=1, le=10),
    offset: int = Query(0, ge=0),
    limit: int = Query(4, ge=1, le=10)
):
    """Generate betting tickets using Gemini AI with 3 games per ticket"""
    
    # Load REAL fixtures
    fixtures_df = load_fixtures()
    
    if fixtures_df.empty:
        return {"offset": 0, "limit": limit, "total": 0, "items": [], "summary": {"total_stake": 0}}
    
    # Filter by date
    try:
        target_date = pd.to_datetime(date)
        fixtures_df = fixtures_df[
            (fixtures_df['Date'] >= target_date) & 
            (fixtures_df['Date'] <= target_date + pd.Timedelta(days=3))
        ]
    except:
        pass
    
    # Analyze all matches
    all_matches = []
    for _, row in fixtures_df.iterrows():
        try:
            analysis = analyze_match(row)
            all_matches.append(analysis)
        except Exception as e:
            continue
    
    if not all_matches:
        return {"offset": 0, "limit": limit, "total": 0, "items": [], "summary": {"total_stake": 0}}
    
    # Use Gemini AI to generate optimal tickets
    gemini = get_gemini_service()
    tickets = await gemini.generate_tickets(all_matches, min_confidence)
    
    # Limit to max_tickets
    tickets = tickets[:max_tickets]
    
    # Summary
    total_stake = len(tickets) * 100
    total_potential = sum(t.get('potential_return', 0) for t in tickets)
    
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
            "max_tickets_per_fixture": max_tickets,
            "matches_analyzed": len(all_matches),
            "ai_powered": True
        }
    }
