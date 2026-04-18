import os
import time
import sys

# Mocking the environment for the script
sys.path.append("/Users/shivm/Workspace/soccer-gpt-api/ai-service")
import inference
import prompts

def benchmark():
    # Mocking a batch of 10 matches
    mock_batch = """
    1. Arsenal vs Chelsea
    2. Real Madrid vs Getafe
    3. Bayern vs Bochum
    4. Juve vs Lazio
    5. PSG vs Nice
    6. Milan vs Inter
    7. Barca vs Betis
    8. Dortmund vs Wolfsburg
    9. Liverpool vs Everton
    10. Spurs vs Arsenal
    """
    
    print("Starting Timing Audit (Local Mistral 7B on MPS)...")
    
    start_time = time.time()
    try:
        # We run the actual inference function
        # Using a lower token limit for the benchmark to prevent infinity loops
        inference.run_inference(
            system_prompt=prompts.MATCH_ANALYSIS_SYSTEM_PROMPT,
            user_content=f"MATCH BATCH DATA (JSON):\n{mock_batch}",
            max_new_tokens=500
        )
        end_time = time.time()
        
        duration = end_time - start_time
        print(f"\nAudit Result:")
        print(f"Time for 1 Batch (10 Matches): {duration:.2f} seconds")
        print(f"Projected Time for 83 Matches (9 Batches): {duration * 9:.2f} seconds")
        
    except Exception as e:
        print(f"Audit Failed: {e}")

if __name__ == "__main__":
    benchmark()
