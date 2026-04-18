import os
import time
import sys
import sqlite3
import json
from datetime import datetime
from google import genai
from google.genai import types

# Setup paths
PROJECT_ROOT = "/Users/shivm/Workspace/soccer-gpt-api"
sys.path.append(os.path.join(PROJECT_ROOT, "ai-service"))
import prompts

# Gemini Configuration
GEMINI_API_KEY = "AIzaSyCIJDTc1JWIzVkYmC3XIlBN9Hfx5kYJoGU"
GEMINI_MODEL = "gemini-2.0-flash"

# Database Configuration
DB_PATH = os.path.join(PROJECT_ROOT, "src/soccer-ai-api/data/soccer.db")

def get_real_matches(limit=10):
    try:
        conn = sqlite3.connect(DB_PATH)
        cursor = conn.cursor()
        query = """
        SELECT f.ApiId, t1.Name, t2.Name FROM Fixtures f
        JOIN Teams t1 ON f.HomeTeamId = t1.ApiId
        JOIN Teams t2 ON f.AwayTeamId = t2.ApiId
        LIMIT ?;
        """
        cursor.execute(query, (limit,))
        rows = cursor.fetchall()
        conn.close()
        return "\n".join([f"{i+1}. {r[1]} vs {r[2]} (ID: {r[0]})" for i, r in enumerate(rows)])
    except:
        return "1. Arsenal vs Chelsea"

def benchmark_gemini(matches_text):
    print(f"Starting Gemini Audit ({GEMINI_MODEL})...")
    client = genai.Client(api_key=GEMINI_API_KEY)
    start_time = time.time()
    try:
        prompt = f"{prompts.MATCH_ANALYSIS_SYSTEM_PROMPT}\n\nMATCH BATCH DATA (JSON):\n{matches_text}"
        response = client.models.generate_content(
            model=GEMINI_MODEL,
            contents=prompt,
            config=types.GenerateContentConfig(
                temperature=0.05,
                response_mime_type="application/json"
            )
        )
        duration = time.time() - start_time
        print(f"Gemini Duration: {duration:.2f} seconds")
        return duration
    except Exception as e:
        print(f"Gemini Failed: {e}")
        return None

if __name__ == "__main__":
    matches = get_real_matches(10)
    benchmark_gemini(matches)
