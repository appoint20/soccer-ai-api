"""
Derby Match Detector
Identifies derby/rivalry matches between local teams
"""
from typing import Dict, Set, Tuple


class DerbyDetector:
    """Detects derby matches between rival teams"""
    
    # Derby pairs by league - teams that are considered local rivals
    DERBIES = {
        # Premier League
        ('Manchester United', 'Manchester City'): 'Manchester Derby',
        ('Liverpool', 'Everton'): 'Merseyside Derby',
        ('Arsenal', 'Tottenham'): 'North London Derby',
        ('Chelsea', 'Arsenal'): 'London Derby',
        ('Chelsea', 'Tottenham'): 'London Derby',
        ('West Ham', 'Tottenham'): 'London Derby',
        ('Newcastle', 'Sunderland'): 'Tyne-Wear Derby',
        ('Aston Villa', 'Birmingham'): 'Second City Derby',
        ('Brighton', 'Crystal Palace'): 'M23 Derby',
        
        # La Liga
        ('Real Madrid', 'Barcelona'): 'El Clásico',
        ('Real Madrid', 'Atletico Madrid'): 'Madrid Derby',
        ('Barcelona', 'Espanyol'): 'Barcelona Derby',
        ('Sevilla', 'Real Betis'): 'Seville Derby',
        ('Athletic Bilbao', 'Real Sociedad'): 'Basque Derby',
        
        # Bundesliga
        ('Bayern Munich', 'Dortmund'): 'Der Klassiker',
        ('Schalke', 'Dortmund'): 'Revierderby',
        ('Hamburg', 'St Pauli'): 'Hamburg Derby',
        ('Cologne', 'Leverkusen'): 'Rhein Derby',
        ('Monchengladbach', 'Cologne'): 'Rhein Derby',
        
        # Serie A
        ('Inter', 'AC Milan'): 'Derby della Madonnina',
        ('Juventus', 'Torino'): 'Derby della Mole',
        ('Lazio', 'Roma'): 'Derby della Capitale',
        ('Napoli', 'Roma'): 'Derby del Sole',
        ('Inter', 'Juventus'): 'Derby d\'Italia',
        
        # Ligue 1
        ('PSG', 'Marseille'): 'Le Classique',
        ('Lyon', 'St Etienne'): 'Derby Rhône-Alpes',
        ('Monaco', 'Nice'): 'Côte d\'Azur Derby',
        
        # Championship
        ('Leeds', 'Manchester United'): 'Roses Rivalry',
        ('Birmingham', 'West Brom'): 'West Midlands Derby',
        ('Nottm Forest', 'Derby'): 'East Midlands Derby',
    }
    
    def __init__(self):
        """Initialize derby detector with normalized team names"""
        self.derby_map = {}
        
        # Create normalized lookup - handle partial matches
        for (team1, team2), derby_name in self.DERBIES.items():
            # Store both directions
            key1 = self._normalize_pair(team1, team2)
            key2 = self._normalize_pair(team2, team1)
            self.derby_map[key1] = derby_name
            self.derby_map[key2] = derby_name
    
    def _normalize_team(self, team_name: str) -> str:
        """Normalize team name for matching"""
        # Remove common suffixes and normalize
        normalized = team_name.lower().strip()
        normalized = normalized.replace(' fc', '').replace(' afc', '')
        normalized = normalized.replace(' united', ' utd')
        normalized = normalized.replace(' city', '')
        return normalized
    
    def _normalize_pair(self, team1: str, team2: str) -> Tuple[str, str]:
        """Create normalized team pair key"""
        t1 = self._normalize_team(team1)
        t2 = self._normalize_team(team2)
        return (t1, t2)
    
    def is_derby(self, home_team: str, away_team: str) -> Tuple[bool, str]:
        """
        Check if match is a derby
        
        Returns:
            (is_derby, derby_name)
        """
        pair = self._normalize_pair(home_team, away_team)
        
        if pair in self.derby_map:
            return (True, self.derby_map[pair])
        
        # Check if teams share city name (additional heuristic)
        home_norm = self._normalize_team(home_team)
        away_norm = self._normalize_team(away_team)
        
        # Simple city match check
        home_words = set(home_norm.split())
        away_words = set(away_norm.split())
        common = home_words & away_words
        
        if common and len(max(common, key=len)) > 3:  # Shared word > 3 chars
            return (True, f"{max(common, key=len).title()} Derby")
        
        return (False, "")
    
    def detect_in_match(self, match: Dict) -> Dict:
        """
        Add derby detection to match data
        
        Args:
            match: Match dictionary with 'home_team' and 'away_team'
            
        Returns:
            Updated match with 'is_derby' and 'derby_name' fields
        """
        home = match.get('home_team', '')
        away = match.get('away_team', '')
        
        is_derby, derby_name = self.is_derby(home, away)
        
        match['is_derby'] = is_derby
        match['derby_name'] = derby_name if is_derby else ''
        
        return match
    
    @staticmethod
    def analyze_derby_performance(matches: list) -> Dict:
        """
        Analyze ML model performance on derby vs non-derby matches
        
        Args:
            matches: List of matches with predictions and results
            
        Returns:
            Dictionary with derby performance statistics
        """
        derby_stats = {
            'total_derbies': 0,
            'correct_derbies': 0,
            'total_non_derbies': 0,
            'correct_non_derbies': 0,
            'derby_names': {},
        }
        
        for match in matches:
            is_derby = match.get('is_derby', False)
            ml_pred = match.get('ml_analysis', {}).get('prediction')
            actual = match.get('actual_result')
            
            if not actual or not ml_pred:
                continue
            
            if is_derby:
                derby_stats['total_derbies'] += 1
                if ml_pred == actual:
                    derby_stats['correct_derbies'] += 1
                
                # Track by derby name
                derby_name = match.get('derby_name', 'Unknown')
                if derby_name not in derby_stats['derby_names']:
                    derby_stats['derby_names'][derby_name] = {'total': 0, 'correct': 0}
                derby_stats['derby_names'][derby_name]['total'] += 1
                if ml_pred == actual:
                    derby_stats['derby_names'][derby_name]['correct'] += 1
            else:
                derby_stats['total_non_derbies'] += 1
                if ml_pred == actual:
                    derby_stats['correct_non_derbies'] += 1
        
        # Calculate accuracies
        if derby_stats['total_derbies'] > 0:
            derby_stats['derby_accuracy'] = derby_stats['correct_derbies'] / derby_stats['total_derbies'] * 100
        else:
            derby_stats['derby_accuracy'] = 0
            
        if derby_stats['total_non_derbies'] > 0:
            derby_stats['non_derby_accuracy'] = derby_stats['correct_non_derbies'] / derby_stats['total_non_derbies'] * 100
        else:
            derby_stats['non_derby_accuracy'] = 0
        
        return derby_stats


# Singleton instance
_derby_detector = None

def get_derby_detector() -> DerbyDetector:
    """Get singleton derby detector instance"""
    global _derby_detector
    if _derby_detector is None:
        _derby_detector = DerbyDetector()
    return _derby_detector
