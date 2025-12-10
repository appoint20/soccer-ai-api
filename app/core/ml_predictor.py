"""
ML Model Predictor - Uses trained XGBoost models with Football API features
"""
import json
import pickle
from pathlib import Path
from typing import Dict, Optional
import numpy as np

MODEL_DIR = Path(__file__).parent.parent.parent / "models"
DATA_DIR = Path(__file__).parent.parent.parent / "data"

# League folders
LEAGUE_FOLDERS = {
    'E0': 'Premier_League', 'E1': 'Championship', 'E2': 'League_One', 'E3': 'League_Two',
    'D1': 'Bundesliga', 'D2': '2_Bundesliga',
    'I1': 'Serie_A', 'I2': 'Serie_B',
    'F1': 'Ligue_1', 'F2': 'Ligue_2',
    'SP1': 'La_Liga'
}


class MLPredictor:
    """Load and use trained XGBoost models for predictions."""
    
    def __init__(self):
        self.hdw_model = None
        self.over25_model = None
        self.btts_model = None
        self.scaler = None
        self.encoder = None
        self.feature_names = []
        self._load_models()
    
    def _load_models(self):
        """Load trained models from disk."""
        try:
            if (MODEL_DIR / "hdw_model.pkl").exists():
                with open(MODEL_DIR / "hdw_model.pkl", 'rb') as f:
                    self.hdw_model = pickle.load(f)
            
            if (MODEL_DIR / "over25_model.pkl").exists():
                with open(MODEL_DIR / "over25_model.pkl", 'rb') as f:
                    self.over25_model = pickle.load(f)
            
            if (MODEL_DIR / "btts_model.pkl").exists():
                with open(MODEL_DIR / "btts_model.pkl", 'rb') as f:
                    self.btts_model = pickle.load(f)
            
            if (MODEL_DIR / "scaler.pkl").exists():
                with open(MODEL_DIR / "scaler.pkl", 'rb') as f:
                    self.scaler = pickle.load(f)
            
            if (MODEL_DIR / "hdw_encoder.pkl").exists():
                with open(MODEL_DIR / "hdw_encoder.pkl", 'rb') as f:
                    self.encoder = pickle.load(f)
            
            if (MODEL_DIR / "model_metadata.json").exists():
                with open(MODEL_DIR / "model_metadata.json", 'r') as f:
                    meta = json.load(f)
                    self.feature_names = meta.get("feature_names", [])
                    
            print(f"✅ Loaded ML models ({len(self.feature_names)} features)")
        except Exception as e:
            print(f"⚠️ Error loading ML models: {e}")
    
    def _extract_team_features(self, stats: Dict, prefix: str) -> Dict:
        """Extract features from team stats JSON."""
        if not stats:
            return {
                f'{prefix}_played': 0, f'{prefix}_wins': 0, f'{prefix}_draws': 0, f'{prefix}_losses': 0,
                f'{prefix}_win_rate': 0.33, f'{prefix}_draw_rate': 0.33, f'{prefix}_loss_rate': 0.33,
                f'{prefix}_goals_for': 0, f'{prefix}_goals_against': 0, f'{prefix}_goal_diff': 0,
                f'{prefix}_avg_goals_for': 1.2, f'{prefix}_avg_goals_against': 1.2,
                f'{prefix}_clean_sheets': 0, f'{prefix}_clean_sheet_rate': 0,
                f'{prefix}_form_points': 5, f'{prefix}_home_wins': 0, f'{prefix}_away_wins': 0,
            }
        
        fixtures = stats.get('fixtures', {})
        goals = stats.get('goals', {})
        
        played = fixtures.get('played', {}).get('total', 1)
        wins = fixtures.get('wins', {}).get('total', 0)
        draws = fixtures.get('draws', {}).get('total', 0)
        losses = fixtures.get('loses', {}).get('total', 0)
        
        goals_for = goals.get('for', {}).get('total', {}).get('total', 0)
        goals_against = goals.get('against', {}).get('total', {}).get('total', 0)
        
        avg_for = float(goals.get('for', {}).get('average', {}).get('total', '1.2') or '1.2')
        avg_against = float(goals.get('against', {}).get('average', {}).get('total', '1.2') or '1.2')
        
        clean_sheets = stats.get('clean_sheet', {}).get('total', 0) or 0
        
        form_str = stats.get('form', '')[-5:]
        form_points = sum({'W': 3, 'D': 1, 'L': 0}.get(c, 0) for c in form_str)
        
        return {
            f'{prefix}_played': played,
            f'{prefix}_wins': wins,
            f'{prefix}_draws': draws,
            f'{prefix}_losses': losses,
            f'{prefix}_win_rate': wins / max(played, 1),
            f'{prefix}_draw_rate': draws / max(played, 1),
            f'{prefix}_loss_rate': losses / max(played, 1),
            f'{prefix}_goals_for': goals_for,
            f'{prefix}_goals_against': goals_against,
            f'{prefix}_goal_diff': goals_for - goals_against,
            f'{prefix}_avg_goals_for': avg_for,
            f'{prefix}_avg_goals_against': avg_against,
            f'{prefix}_clean_sheets': clean_sheets,
            f'{prefix}_clean_sheet_rate': clean_sheets / max(played, 1),
            f'{prefix}_form_points': form_points,
            f'{prefix}_home_wins': fixtures.get('wins', {}).get('home', 0),
            f'{prefix}_away_wins': fixtures.get('wins', {}).get('away', 0),
        }
    
    def _build_features(self, home_stats: Dict, away_stats: Dict, odds: Dict = None, h2h: Dict = None) -> list:
        """Build feature vector matching training (59 features including H2H)."""
        features = {}
        
        # Team features (34 features: 17 home + 17 away)
        features.update(self._extract_team_features(home_stats, 'home'))
        features.update(self._extract_team_features(away_stats, 'away'))
        
        # H2H features (10 features) - use defaults if not provided
        h2h = h2h or {}
        features['h2h_matches'] = h2h.get('matches', 0)
        features['h2h_home_wins'] = h2h.get('home_wins', 0)
        features['h2h_draws'] = h2h.get('draws', 0)
        features['h2h_away_wins'] = h2h.get('away_wins', 0)
        features['h2h_home_win_rate'] = h2h.get('home_win_rate', 0.33)
        features['h2h_draw_rate'] = h2h.get('draw_rate', 0.33)
        features['h2h_away_win_rate'] = h2h.get('away_win_rate', 0.33)
        features['h2h_avg_goals'] = h2h.get('avg_goals', 2.5)
        features['h2h_btts_rate'] = h2h.get('btts_rate', 0.5)
        features['h2h_over25_rate'] = h2h.get('over25_rate', 0.5)
        
        # Odds features
        odds = odds or {}
        features['odds_home'] = odds.get('home', 2.0)
        features['odds_draw'] = odds.get('draw', 3.3)
        features['odds_away'] = odds.get('away', 3.5)
        
        features['implied_home'] = 1 / features['odds_home']
        features['implied_draw'] = 1 / features['odds_draw']
        features['implied_away'] = 1 / features['odds_away']
        
        features['odds_spread'] = features['odds_away'] - features['odds_home']
        features['bookmaker_margin'] = features['implied_home'] + features['implied_draw'] + features['implied_away'] - 1
        
        # Comparative features
        features['win_rate_diff'] = features['home_win_rate'] - features['away_win_rate']
        features['goal_diff_diff'] = features['home_goal_diff'] - features['away_goal_diff']
        features['avg_goals_diff'] = features['home_avg_goals_for'] - features['away_avg_goals_for']
        features['form_diff'] = features['home_form_points'] - features['away_form_points']
        
        # Over 2.5 features
        features['over25_odds'] = odds.get('over25', 1.9)
        features['under25_odds'] = odds.get('under25', 2.0)
        features['expected_total_goals'] = features['home_avg_goals_for'] + features['away_avg_goals_for']
        
        # Build vector in correct order from model metadata
        if self.feature_names:
            vector = [features.get(name, 0) for name in self.feature_names]
        else:
            vector = list(features.values())
        return vector
    
    def predict(self, home_stats: Dict, away_stats: Dict, odds: Dict = None, h2h: Dict = None) -> Dict:
        """Make prediction using ML models."""
        if not self.hdw_model:
            return self._fallback_prediction(home_stats, away_stats)
        
        try:
            # Build features (59 total including H2H)
            features = self._build_features(home_stats, away_stats, odds, h2h)
            
            # Scale
            if self.scaler:
                features = self.scaler.transform([features])
            else:
                features = [features]
            
            # Predict HDW
            proba = self.hdw_model.predict_proba(features)[0]
            pred_idx = np.argmax(proba)
            
            if self.encoder:
                prediction = self.encoder.inverse_transform([pred_idx])[0]
            else:
                prediction = ['A', 'D', 'H'][pred_idx]
            
            return {
                'prediction': prediction,
                'confidence': float(max(proba)),
                'home_win': float(proba[2]) if len(proba) > 2 else 0.33,  # H is usually last
                'draw': float(proba[1]) if len(proba) > 1 else 0.33,
                'away_win': float(proba[0]) if len(proba) > 0 else 0.33
            }
        except Exception as e:
            print(f"⚠️ ML prediction error: {e}")
            return self._fallback_prediction(home_stats, away_stats)
    
    def _fallback_prediction(self, home_stats: Dict, away_stats: Dict) -> Dict:
        """Simple fallback when model fails."""
        home_strength = home_stats.get('avg_goals_scored', 1.3) - away_stats.get('avg_goals_conceded', 1.3)
        away_strength = away_stats.get('avg_goals_scored', 1.1) - home_stats.get('avg_goals_conceded', 1.1)
        diff = home_strength - away_strength + 0.3
        
        if diff > 0.3:
            return {'prediction': 'H', 'confidence': 0.5, 'home_win': 0.5, 'draw': 0.3, 'away_win': 0.2}
        elif diff < -0.3:
            return {'prediction': 'A', 'confidence': 0.5, 'home_win': 0.2, 'draw': 0.3, 'away_win': 0.5}
        else:
            return {'prediction': 'D', 'confidence': 0.4, 'home_win': 0.35, 'draw': 0.3, 'away_win': 0.35}


_ml_predictor = None

def get_ml_predictor() -> MLPredictor:
    global _ml_predictor
    if _ml_predictor is None:
        _ml_predictor = MLPredictor()
    return _ml_predictor
