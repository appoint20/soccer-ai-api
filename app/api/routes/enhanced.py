"""
Enhanced Predictions API Route

Combines API-Football predictions with ML, Monte Carlo, and detectors,
then analyzes with Gemini per league.
"""
from fastapi import APIRouter, HTTPException
from typing import Optional

from app.models.enhanced_predictions import (
    EnhancedPredictionsRequest,
    EnhancedPredictionsResponse
)
from app.services.enhanced_predictions import EnhancedPredictionsService, LEAGUE_FOLDERS


router = APIRouter(prefix="/predictions", tags=["Predictions"])


@router.post("/enhanced", response_model=dict)
async def get_enhanced_predictions(request: EnhancedPredictionsRequest):
    """
    Get enhanced predictions combining API-Football + ML + MC + Detectors + Gemini.
    
    This endpoint:
    1. Loads API-Football prediction JSON files for the specified date
    2. Enhances each prediction with ML, Monte Carlo, and trap detection
    3. Groups matches by league
    4. Sends each league's matches to Gemini for AI analysis
    5. Returns complete enhanced predictions with Gemini insights
    
    Args:
        request: EnhancedPredictionsRequest with date and optional league_id
    
    Returns:
        Dictionary with enhanced predictions grouped by league
    
    Example:
        POST /api/v1/predictions/enhanced
        {
            "date": "2025-12-13",
            "league_id": "E0"  // Optional
        }
    """
    try:
        service = EnhancedPredictionsService()
        
        # Determine league folder to load
        # Empty string should be treated as None (no filter)
        league_folder = None
        if request.league_id and request.league_id.strip() and request.league_id in LEAGUE_FOLDERS:
            league_folder = LEAGUE_FOLDERS[request.league_id]
        
        # Load API-Football predictions
        api_predictions = service.load_api_predictions(request.date, league_folder)
        
        if not api_predictions:
            return {
                "date": request.date,
                "leagues": {},
                "total_matches": 0,
                "total_leagues": 0,
                "message": f"No API-Football predictions found for {request.date}"
            }
        
        # Enhance each prediction
        enhanced_predictions = []
        for api_pred in api_predictions:
            try:
                enhanced = service.enhance_prediction(api_pred)
                enhanced_predictions.append(enhanced)
            except Exception as e:
                print(f"Error enhancing prediction for {api_pred.get('home_team')} vs {api_pred.get('away_team')}: {e}")
                continue
        
        # Group by league
        leagues_data = service.group_by_league(enhanced_predictions)
        
        # Analyze each league with Gemini
        for league_folder, league_data in leagues_data.items():
            try:
                gemini_analysis = service.analyze_with_gemini(league_data)
                league_data['gemini_analysis'] = gemini_analysis
            except Exception as e:
                print(f"Error in Gemini analysis for {league_folder}: {e}")
                league_data['gemini_analysis'] = {
                    'summary': f'Analysis unavailable: {str(e)}',
                    'top_picks': [],
                    'warnings': []
                }
        
        # Build response
        response = {
            "date": request.date,
            "leagues": leagues_data,
            "total_matches": len(enhanced_predictions),
            "total_leagues": len(leagues_data)
        }
        
        return response
        
    except Exception as e:
        raise HTTPException(
            status_code=500,
            detail=f"Error processing enhanced predictions: {str(e)}"
        )


@router.get("/enhanced/available-dates")
async def get_available_dates():
    """
    Get list of dates that have API-Football predictions available.
    
    Returns:
        List of dates with available predictions
    """
    try:
        from pathlib import Path
        from app.services.enhanced_predictions import PREDICTIONS_DIR
        
        dates = set()
        
        # Scan all league folders for date patterns
        if PREDICTIONS_DIR.exists():
            for league_dir in PREDICTIONS_DIR.iterdir():
                if league_dir.is_dir():
                    for json_file in league_dir.glob("*.json"):
                        # Extract date from filename (format: YYYY-MM-DD_*.json)
                        filename = json_file.stem
                        if '_' in filename:
                            date_part = filename.split('_')[0]
                            dates.add(date_part)
        
        return {
            "available_dates": sorted(list(dates)),
            "total_dates": len(dates)
        }
        
    except Exception as e:
        raise HTTPException(
            status_code=500,
            detail=f"Error listing available dates: {str(e)}"
        )
