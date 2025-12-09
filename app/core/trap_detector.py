"""
Trap Detector - Identifies risky betting patterns
"""
from typing import List, Dict, Any


class TrapDetector:
    """Detects trap bets: 0-0 games, odds traps, defensive matchups"""
    
    TRAP_FLAGS = {
        'ODDS_TRAP': 'Heavy favorite with suspicious odds movement',
        'DEFENSIVE_TEAMS': 'Both teams average under 1 goal per game',
        'H2H_LOW_SCORING': 'Historical H2H shows 60%+ low-scoring games',
        'DRAW_PRONE': 'H2H shows 40%+ draws between these teams',
        'AWAY_STRONG': 'Away team stronger but odds favor home',
        'END_OF_SEASON': 'Match near season end with little at stake',
        'BOTH_FORM_BAD': 'Both teams on losing streaks',
    }
    
    def detect(self, match_data: Dict[str, Any]) -> Dict[str, Any]:
        """
        Analyze match for trap indicators.
        
        Returns:
            {
                "is_trap": bool,
                "warning_level": "NONE" | "LOW" | "MEDIUM" | "HIGH",
                "flags": [],
                "message": str | None
            }
        """
        flags = []
        
        home_stats = match_data.get('home_stats', {})
        away_stats = match_data.get('away_stats', {})
        h2h = match_data.get('h2h', {})
        odds = match_data.get('odds', {})
        
        # Check defensive teams (low scoring)
        home_avg = home_stats.get('avg_goals_scored', 1.5)
        away_avg = away_stats.get('avg_goals_scored', 1.5)
        
        if home_avg < 1.0 and away_avg < 1.0:
            flags.append('DEFENSIVE_TEAMS')
        
        # Check H2H for low scoring
        h2h_under_2_rate = h2h.get('under_2_rate', 0)
        if h2h_under_2_rate > 0.6:
            flags.append('H2H_LOW_SCORING')
        
        # Check H2H for draw-prone
        h2h_draw_rate = h2h.get('draw_rate', 0)
        if h2h_draw_rate > 0.4:
            flags.append('DRAW_PRONE')
        
        # Check odds trap (heavy favorite)
        home_odds = odds.get('home', 2.0)
        away_odds = odds.get('away', 2.0)
        
        if home_odds < 1.4 or away_odds < 1.4:
            # Heavy favorite - potential trap
            favorite_is_home = home_odds < away_odds
            favorite_stats = home_stats if favorite_is_home else away_stats
            
            # If favorite has poor recent form
            form = favorite_stats.get('form', [])
            recent_losses = sum(1 for r in form[:3] if r == 'L')
            if recent_losses >= 2:
                flags.append('ODDS_TRAP')
        
        # Check bad form for both teams
        home_form = home_stats.get('form', [])
        away_form = away_stats.get('form', [])
        
        home_losses = sum(1 for r in home_form[:5] if r == 'L')
        away_losses = sum(1 for r in away_form[:5] if r == 'L')
        
        if home_losses >= 3 and away_losses >= 3:
            flags.append('BOTH_FORM_BAD')
        
        # Determine warning level
        num_flags = len(flags)
        if num_flags == 0:
            warning_level = 'NONE'
        elif num_flags == 1:
            warning_level = 'LOW'
        elif num_flags == 2:
            warning_level = 'MEDIUM'
        else:
            warning_level = 'HIGH'
        
        is_trap = num_flags >= 2
        
        # Generate message
        message = None
        if is_trap:
            flag_descriptions = [self.TRAP_FLAGS.get(f, f) for f in flags]
            message = f"⚠️ Warning: {', '.join(flag_descriptions)}"
        
        return {
            'is_trap': is_trap,
            'warning_level': warning_level,
            'flags': flags,
            'message': message
        }
