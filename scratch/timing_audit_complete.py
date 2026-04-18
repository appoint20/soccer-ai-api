import os
import time
import sys
import sqlite3
import json
from datetime import datetime

# Setup paths for local inference imports
PROJECT_ROOT = "/Users/shivm/Workspace/soccer-gpt-api"
sys.path.append(os.path.join(PROJECT_ROOT, "ai-service"))

import inference
import prompts

# Gemini Configuration
GEMINI_API_KEY = "AIzaSyCIJDTc1JWIzVkYmC3XIlBN9Hfx5kYJoGU"
GEMINI_MODEL = "gemini-2.0-flash" # Use flash for speed comparison

# Database Configuration
DB_PATH = os.path.join(PROJECT_ROOT, "src/soccer-ai-api/data/soccer.db")

def get_real_matches(limit=10):
    """Load real matches from the database and format them for the prompt."""
    try:
        conn = sqlite3.connect(DB_PATH)
        cursor = conn.cursor()
        
        # Join Fixtures and Teams to get names
        query = """
        SELECT 
            f.ApiId, 
            t1.Name as HomeTeam, 
            t2.Name as AwayTeam
        FROM Fixtures f
        JOIN Teams t1 ON f.HomeTeamId = t1.ApiId
        JOIN Teams t2 ON f.AwayTeamId = t2.ApiId
        ORDER BY f.Date DESC
        LIMIT ?;
        """
        cursor.execute(query, (limit,))
        rows = cursor.fetchall()
        conn.close()
        
        matches = []
        for i, row in enumerate(rows, 1):
            matches.append(f"{i}. {row[1]} vs {row[2]} (ID: {row[0]})")
        
        return "\n".join(matches)
    except Exception as e:
        print(f"Error loading matches from DB: {e}")
        return "1. Arsenal vs Chelsea\n2. Real Madrid vs Getafe\n3. Bayern vs Bochum"

def benchmark_local(matches_text):
    print(f"\n[{datetime.now().strftime('%H:%M:%S')}] Starting Local Model Audit (Mistral-7B on MPS)...")
    start_time = time.time()
    try:
        # Simulate batch analysis logic from LocalAiAnalysisService
        # We use a batch of 10 matches (typical production size)
        raw_output = inference.run_inference(
            system_prompt=prompts.MATCH_ANALYSIS_SYSTEM_PROMPT,
            user_content=f"MATCH BATCH DATA (JSON):\n{matches_text}",
            model_key="mistral",
            max_new_tokens=2000 # Increased for realistic response size
        )
        duration = time.time() - start_time
        print(f"Local Audit Success. Duration: {duration:.2f} seconds")
        return duration
    except Exception as e:
        print(f"Local Audit Failed: {e}")
        return None

def benchmark_gemini(matches_text):
    print(f"\n[{datetime.now().strftime('%H:%M:%S')}] Starting Gemini Audit ({GEMINI_MODEL})...")
    
    # Import here to ensure dependencies are met
    from google import genai
    from google.genai import types

    client = genai.Client(api_key=GEMINI_API_KEY)
    
    start_time = time.time()
    try:
        # Mimic the LegacyExternalAiService prompt structure
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
        print(f"Gemini Audit Success. Duration: {duration:.2f} seconds")
        return duration
    except Exception as e:
        print(f"Gemini Audit Failed: {e}")
        return None

def run_audit():
    print("="*60)
    print("SOCCER AI - MATCH ANALYSIS PERFORMANCE AUDIT")
    print(f"Date: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print("="*60)
    
    matches = get_real_matches(10)
    print(f"Input: Batch of 10 matches from database.")
    
    # 1. Local Model
    local_time = benchmark_local(matches)
    
    # 2. Gemini
    gemini_time = benchmark_gemini(matches)
    
    # Summary Table
    print("\n" + "="*60)
    print(f"{'Model':<20} | {'Latency (10 Matches)':<25} | {'Speed Index'}")
    print("-" * 60)
    
    if local_time and gemini_time:
        ratio = local_time / gemini_time
        print(f"{'Mistral-7B (Local)':<20} | {local_time:>20.2f}s | {'1.00x'}")
        print(f"{'Gemini 2.0 Flash':<20} | {gemini_time:>20.2f}s | {local_time/gemini_time:>10.2f}x faster")
        
        print("-" * 60)
        print(f"Projected Sync Time (100 matches):")
        print(f" - Local:  {local_time * 10 / 60:.2f} minutes")
        print(f" - Gemini: {gemini_time * 10 / 60:.2f} minutes")
    else:
        print("Comparison failed due to model errors.")
    
    print("="*60)

if __name__ == "__main__":
    run_audit()
