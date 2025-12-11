"""
H2H Service - Calculate Head-to-Head stats from historical Excel data
"""
import pandas as pd
from pathlib import Path
from typing import Dict, List, Optional
from functools import lru_cache

DATA_DIR = Path(__file__).parent.parent.parent / "data"

class H2HService:
    """
    Service to retrieve and calculate Head-to-Head (H2H) statistics 
    from historical match data.
    """
    
    _instance = None
    
    def __new__(cls):
        if cls._instance is None:
            cls._instance = super(H2HService, cls).__new__(cls)
            cls._instance.df = None
        return cls._instance

    def _load_data(self):
        """Load and combine all historical Excel files."""
        if self.df is not None:
            return

        historical_dir = DATA_DIR / "historical"
        if not historical_dir.exists():
            print("Warning: Historical directory not found.")
            self.df = pd.DataFrame()
            return

        files = list(historical_dir.glob("all-euro-data-*.xlsx"))
        dfs = []
        
        for file in files:
            try:
                # Read specific columns to save memory
                df = pd.read_excel(file, usecols=['Date', 'HomeTeam', 'AwayTeam', 'FTHG', 'FTAG', 'FTR', 'Div'])
                df = df.rename(columns={'Div': 'League'})
                dfs.append(df)
            except Exception as e:
                print(f"Error reading {file}: {e}")
                continue
        
        if dfs:
            self.df = pd.concat(dfs, ignore_index=True)
            self.df['Date'] = pd.to_datetime(self.df['Date'], errors='coerce')
            self.df = self.df.sort_values('Date', ascending=False) # Newest first
        else:
            self.df = pd.DataFrame()

    def get_h2h_stats(self, home_team: str, away_team: str, limit: int = 5) -> Dict:
        """
        Get H2H statistics for two teams.
        
        Args:
            home_team: Name of the home team
            away_team: Name of the away team
            limit: Number of last matches to analyze
            
        Returns:
            Dictionary containing H2H stats
        """
        self._load_data()
        
        if self.df.empty:
            return self._empty_h2h()

        # Filter matches where both teams played against each other
        mask = (
            ((self.df['HomeTeam'] == home_team) & (self.df['AwayTeam'] == away_team)) |
            ((self.df['HomeTeam'] == away_team) & (self.df['AwayTeam'] == home_team))
        )
        
        h2h_matches = self.df[mask].head(limit)
        
        if h2h_matches.empty:
            return self._empty_h2h()

        matches_data = []
        home_wins = 0
        away_wins = 0
        draws = 0
        draws = 0
        total_goals = 0
        total_home_goals = 0
        total_away_goals = 0
        
        for _, match in h2h_matches.iterrows():
            match_date = match['Date'].strftime('%Y-%m-%d') if pd.notna(match['Date']) else 'Unknown'
            score = f"{int(match['FTHG'])}-{int(match['FTAG'])}"
            winner = match['FTR']
            
            # Determine winner relative to the request context
            # If HomeTeam (in request) was Home in match and won (H) -> Home Win
            # If HomeTeam (in request) was Away in match and won (A) -> Home Win
            is_home_match_home = (match['HomeTeam'] == home_team)
            
            if winner == 'D':
                draws += 1
                result = "Draw"
            elif (is_home_match_home and winner == 'H') or (not is_home_match_home and winner == 'A'):
                home_wins += 1
                result = f"{home_team} Win"
            else:
                away_wins += 1
                result = f"{away_team} Win"
                
            total_goals += (match['FTHG'] + match['FTAG'])
            
            # Calculate team specific goals for average score
            if match['HomeTeam'] == home_team:
                total_home_goals += match['FTHG']
                total_away_goals += match['FTAG']
            else:
                total_home_goals += match['FTAG']
                total_away_goals += match['FTHG']
            
            matches_data.append({
                "date": match_date,
                "home_team": match['HomeTeam'],
                "away_team": match['AwayTeam'],
                "score": score,
                "winner": result,
                "competition": match.get('Div', 'Unknown')
            })

        matches_count = len(matches_data)
        avg_home = round(total_home_goals / matches_count, 1)
        avg_away = round(total_away_goals / matches_count, 1)
        
        return {
            "total_matches": matches_count,
            "home_wins": home_wins,
            "away_wins": away_wins,
            "draws": draws,
            "home_win_rate": round(home_wins / matches_count, 2),
            "away_win_rate": round(away_wins / matches_count, 2),
            "draw_rate": round(draws / matches_count, 2),
            "avg_goals": round(total_goals / matches_count, 2),
            "average_score": f"{avg_home}-{avg_away}",
            "avg_home_goals": avg_home,
            "avg_away_goals": avg_away,
            "matches": matches_data
        }

    def _empty_h2h(self) -> Dict:
        return {
            "total_matches": 0,
            "home_wins": 0,
            "away_wins": 0,
            "draws": 0,
            "home_win_rate": 0,
            "away_win_rate": 0,
            "draw_rate": 0,
            "draw_rate": 0,
            "avg_goals": 0,
            "average_score": "0.0-0.0",
            "avg_home_goals": 0,
            "avg_away_goals": 0,
            "matches": []
        }

def get_h2h_service() -> H2HService:
    return H2HService()
