"""
Enhanced Predictions Service

Combines API-Football predictions with ML, Monte Carlo, and detector predictions.
Groups by league and sends to Gemini for analysis.
"""
from pathlib import Path
from typing import Dict, List, Optional
from datetime import datetime
import json

from app.core.ml_predictor import get_ml_predictor
from app.core.monte_carlo import MonteCarloSimulator
from app.core.trap_detector import TrapDetector
from app.services.team_stats import get_team_stats_service
from app.core.derby_detector import get_derby_detector
from app.core.fixture_congestion import add_congestion_features_to_match
from app.services.gemini_service import get_gemini_service


# Paths
PROJECT_ROOT = Path(__file__).parent.parent.parent
DATA_DIR = PROJECT_ROOT / "data"
PREDICTIONS_DIR = DATA_DIR / "predictions"

# League folder mapping
LEAGUE_FOLDERS = {
    'E0': 'Premier_League',
    'E1': 'Championship',
    'E2': 'League_One',
    'E3': 'League_Two',
    'D1': 'Bundesliga',
    'D2': '2_Bundesliga',
    'I1': 'Serie_A',
    'I2': 'Serie_B',
    'F1': 'Ligue_1',
    'F2': 'Ligue_2',
    'SP1': 'La_Liga'
}

LEAGUE_NAMES = {
    'Premier_League': 'Premier League',
    'Championship': 'Championship',
    'League_One': 'League One',
    'League_Two': 'League Two',
    'Bundesliga': 'Bundesliga',
    '2_Bundesliga': '2. Bundesliga',
    'Serie_A': 'Serie A',
    'Serie_B': 'Serie B',
    'Ligue_1': 'Ligue 1',
    'Ligue_2': 'Ligue 2',
    'La_Liga': 'La Liga'
}


class EnhancedPredictionsService:
    """Service for loading and enhancing API-Football predictions."""
    
    def __init__(self):
        self.ml_predictor = get_ml_predictor()
        self.monte_carlo = MonteCarloSimulator()
        self.trap_detector = TrapDetector()
        self.stats_service = get_team_stats_service()
        self.derby_detector = get_derby_detector()
        self.gemini_service = get_gemini_service()
        
        # Initialize H2H service
        from app.services.h2h_service import get_h2h_service
        self.h2h_service = get_h2h_service()
        
        # Ensure H2H data is loaded
        if self.h2h_service.df is None:
            self.h2h_service._load_data()
    
    def load_api_predictions(self, date: str, league_folder: Optional[str] = None) -> List[dict]:
        """
        Load API-Football prediction JSON files for a given date.
        Only loads predictions for supported leagues (from leagues.json).
        
        Args:
            date: Date in YYYY-MM-DD format
            league_folder: Optional league folder name (e.g., 'Premier_League')
        
        Returns:
            List of API prediction dictionaries
        """
        # Load supported leagues from leagues.json
        from pathlib import Path
        import json
        
        leagues_file = PROJECT_ROOT / "data" / "leagues.json"
        supported_league_ids = set()
        
        if leagues_file.exists():
            with open(leagues_file, 'r') as f:
                leagues_data = json.load(f)
                supported_league_ids = {league.get('id') for league in leagues_data if league.get('id')}
        
        # Map league IDs to folder names (only supported ones)
        supported_folders = {
            folder for league_id, folder in LEAGUE_FOLDERS.items() 
            if league_id in supported_league_ids
        }
        
        predictions = []
        
        # Determine which leagues to check
        if league_folder:
            # Only process if it's a supported league
            if league_folder in supported_folders:
                league_folders = [league_folder]
            else:
                return []  # Unsupported league requested
        else:
            league_folders = supported_folders
        
        for folder in league_folders:
            league_dir = PREDICTIONS_DIR / folder
            if not league_dir.exists():
                continue
            
            # Find all JSON files for this date
            pattern = f"{date}_*.json"
            for json_file in league_dir.glob(pattern):
                try:
                    with open(json_file, 'r') as f:
                        data = json.load(f)
                        data['_league_folder'] = folder
                        predictions.append(data)
                except Exception as e:
                    print(f"Error loading {json_file}: {e}")
        
        return predictions
    
    def enhance_prediction(self, api_prediction: dict) -> dict:
        """
        Enhance API-Football prediction with ML, MC, and detectors.
        
        Args:
            api_prediction: API-Football prediction dict
        
        Returns:
            Enhanced prediction with all models combined
        """
        home_team = api_prediction.get('home_team')
        away_team = api_prediction.get('away_team')
        match_date = api_prediction.get('date')
        league_folder = api_prediction.get('_league_folder')
        
        # Reverse lookup league ID from folder
        league_id = None
        for lid, folder in LEAGUE_FOLDERS.items():
            if folder == league_folder:
                league_id = lid
                break
        
        if not league_id:
            league_id = 'E0'  # Default
        
        # Get team stats
        home_stats = self.stats_service.get_team_stats(home_team, league_id, before_date=match_date)
        away_stats = self.stats_service.get_team_stats(away_team, league_id, before_date=match_date)
        
        # Get H2H stats
        h2h_stats = self.h2h_service.get_h2h_stats(home_team, away_team)
        
        # Get fixture congestion
        congestion_features = add_congestion_features_to_match(
            home_team, away_team, league_id, match_date, self.h2h_service.df
        )
        
        # Extract odds from API prediction (if available)
        # Note: API-Football predictions don't always include odds
        odds = {
            'home': 2.0,  # Default values
            'draw': 3.3,
            'away': 3.5
        }
        
        # Monte Carlo simulation
        mc_result = self.monte_carlo.simulate(
            home_attack=home_stats.get('avg_goals_scored', 1.3),
            away_attack=away_stats.get('avg_goals_scored', 1.1),
            home_defense=home_stats.get('avg_goals_conceded', 1.1),
            away_defense=away_stats.get('avg_goals_conceded', 1.3)
        )
        
        # ML prediction with MC override
        ml_result = self.ml_predictor.predict(
            home_stats,
            away_stats,
            odds,
            h2h={'matches': h2h_stats.get('matches', 0)},
            congestion=congestion_features,
            mc_override={
                'prediction': mc_result.get('hdw'),
                'confidence': mc_result.get('hdw_confidence', 0)
            }
        )
        
        # Trap detection
        trap_result = self.trap_detector.detect({
            'home_stats': home_stats,
            'away_stats': away_stats,
            'h2h': {'draw_rate': h2h_stats.get('draw_rate', 0.25), 'under_2_rate': 0.3},
            'odds': odds,
            'congestion': {
                'home_congestion_index': congestion_features.get('home_congestion_index', 0),
                'away_congestion_index': congestion_features.get('away_congestion_index', 0)
            }
        })
        
        # Derby detection
        is_derby, derby_name = self.derby_detector.is_derby(home_team, away_team)
        
        # Build consensus (API-Football + ML + MC)
        predictions_list = [
            ml_result.get('prediction'),
            mc_result.get('hdw'),
            self._parse_api_winner(api_prediction.get('api_prediction', {}))
        ]
        
        # Count agreements
        from collections import Counter
        pred_counts = Counter(predictions_list)
        consensus_pred, consensus_count = pred_counts.most_common(1)[0]
        
        # Calculate consensus confidence
        confidences = [
            ml_result.get('confidence', 0),
            mc_result.get('hdw_confidence', 0)
        ]
        consensus_confidence = sum(confidences) / len(confidences)
        
        # Build enhanced prediction object
        enhanced = {
            'fixture_id': api_prediction.get('fixture_id'),
            'date': api_prediction.get('date'),
            'home_team': home_team,
            'away_team': away_team,
            'league': league_folder,
            'league_name': LEAGUE_NAMES.get(league_folder, league_folder),
            
            'api_football': {
                'winner': api_prediction.get('api_prediction', {}).get('winner', {}),
                'advice': api_prediction.get('api_prediction', {}).get('advice'),
                'percent': api_prediction.get('api_prediction', {}).get('percent', {}),
                'goals': api_prediction.get('api_prediction', {}).get('goals', {}),
                'under_over': api_prediction.get('api_prediction', {}).get('under_over'),
                'win_or_draw': api_prediction.get('api_prediction', {}).get('win_or_draw')
            },
            
            'ml_predictions': {
                'prediction': ml_result.get('prediction'),
                'confidence': ml_result.get('confidence'),
                'raw_confidence': ml_result.get('raw_confidence'),
                'mc_overridden': ml_result.get('mc_overridden', False),
                'probabilities': ml_result.get('probabilities', {}),
                'btts': ml_result.get('btts', {}),
                'over_25': ml_result.get('over_25', {})
            },
            
            'monte_carlo': {
                'prediction': mc_result.get('hdw'),
                'confidence': mc_result.get('hdw_confidence'),
                'simulations': 10000,
                'btts_probability': mc_result.get('btts_probability'),
                'over_25_probability': mc_result.get('over_25_probability'),
                'avg_total_goals': mc_result.get('avg_total_goals')
            },
            
            'analysis': {
                'trap_detection': trap_result,
                'is_derby': is_derby,
                'derby_name': derby_name,
                'fixture_congestion': {
                    'home_congestion_index': congestion_features.get('home_congestion_index', 0),
                    'away_congestion_index': congestion_features.get('away_congestion_index', 0)
                },
                'h2h': {
                    'matches': h2h_stats.get('matches', 0),
                    'home_wins': h2h_stats.get('home_wins', 0),
                    'draws': h2h_stats.get('draws', 0),
                    'away_wins': h2h_stats.get('away_wins', 0)
                }
            },
            
            'odds': odds,
            
            'consensus': {
                'prediction': consensus_pred,
                'agreement': f"{consensus_count}/3",
                'confidence': round(consensus_confidence, 2)
            },
            
            'team_stats': {
                'home': home_stats,
                'away': away_stats
            }
        }
        
        return enhanced
    
    def group_by_league(self, predictions: List[dict]) -> Dict[str, dict]:
        """
        Group enhanced predictions by league.
        
        Args:
            predictions: List of enhanced predictions
        
        Returns:
            Dictionary grouped by league folder name
        """
        leagues = {}
        
        for pred in predictions:
            league_folder = pred.get('league')
            if league_folder not in leagues:
                leagues[league_folder] = {
                    'league_name': pred.get('league_name'),
                    'matches': []
                }
            
            leagues[league_folder]['matches'].append(pred)
        
        # Add match count to each league
        for league_data in leagues.values():
            league_data['total_matches'] = len(league_data['matches'])
        
        return leagues
    
    def analyze_with_gemini(self, league_data: dict) -> dict:
        """
        Send league data to Gemini for AI analysis.
        
        Args:
            league_data: Dictionary with league matches
        
        Returns:
            Gemini analysis results
        """
        league_name = league_data.get('league_name', 'Unknown League')
        matches = league_data.get('matches', [])
        
        if not matches:
            return {'summary': 'No matches to analyze', 'top_picks': [], 'warnings': []}
        
        # Build prompt
        prompt = self._build_gemini_prompt(league_name, matches)
        
        try:
            # Call Gemini
            analysis = self.gemini_service.analyze_matches(prompt)
            return analysis
        except Exception as e:
            print(f"Gemini analysis failed: {e}")
            return {
                'summary': f'Analysis unavailable: {str(e)}',
                'top_picks': [],
                'warnings': []
            }
    
    def _parse_api_winner(self, api_prediction: dict) -> str:
        """Parse API-Football winner into H/D/A format."""
        winner = api_prediction.get('winner', {})
        if not winner:
            return 'D'
        
        winner_name = winner.get('name', '')
        if 'draw' in winner_name.lower():
            return 'D'
        
        # Assume winner is home if mentioned first in typical API response
        # This is a simplification - might need adjustment
        return 'H' if winner else 'D'
    
    def _build_gemini_prompt(self, league_name: str, matches: List[dict]) -> str:
        """Build prompt for Gemini analysis."""
        prompt = f"""Analyze these {league_name} matches:

"""
        
        for i, match in enumerate(matches, 1):
            prompt += f"\n{i}. {match['home_team']} vs {match['away_team']}\n"
            prompt += f"   - ML: {match['ml_predictions']['prediction']} ({match['ml_predictions']['confidence']:.1%})\n"
            prompt += f"   - MC: {match['monte_carlo']['prediction']} ({match['monte_carlo']['confidence']:.1%})\n"
            prompt += f"   - API-Football: {match['api_football']['advice']}\n"
            prompt += f"   - Consensus: {match['consensus']['prediction']} ({match['consensus']['agreement']})\n"
            
            if match['analysis']['trap_detection']['is_trap']:
                prompt += f"   - ⚠️ Trap: {', '.join(match['analysis']['trap_detection']['trap_types'])}\n"
            
            if match['analysis']['is_derby']:
                prompt += f"   - ⚡ Derby: {match['analysis']['derby_name']}\n"
        
        prompt += """

Provide:
1. Overall summary of matches
2. Top 3 picks (highest confidence + no traps)
3. Any warnings or concerns
4. Market recommendations (HDW, BTTS, O2.5)

Format as JSON with keys: summary, top_picks, warnings, market_recommendations
"""
        
        return prompt
