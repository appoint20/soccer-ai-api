import json
import sqlite3
import itertools
from datetime import datetime, timedelta

def load_data():
    conn = sqlite3.connect('soccer.db')
    c = conn.cursor()
    c.execute("SELECT id, date, home_goal, away_goal, status, over_25_odds, home_win_odds, away_win_odds, draw_odds FROM fixtures WHERE status = 'FT'")
    fixtures = {r[0]: r for r in c.fetchall()}
    conn.close()
    
    with open('data/analysis_cache.json', 'r') as f:
        # Assuming we have a cache or we can construct one. 
        # Actually, let's just use the backtest_combinations.txt raw output if we can't get JSON.
        pass
