import json
import sys
from collections import defaultdict

def validate_json(file_path):
    try:
        with open(file_path, 'r') as f:
            data = json.load(f)
    except FileNotFoundError:
        print(f"Error: File {file_path} not found.")
        return
    except json.JSONDecodeError:
        print(f"Error: Failed to decode JSON from {file_path}.")
        return

    null_paths = []
    zero_paths = []
    total_matches = len(data.get("matches", []))
    
    print(f"Validating {total_matches} matches...")

    def check_recursive(obj, path):
        if obj is None:
            null_paths.append(path)
        elif isinstance(obj, (int, float)):
            if obj == 0:
                zero_paths.append(path)
        elif isinstance(obj, dict):
            for k, v in obj.items():
                check_recursive(v, f"{path}.{k}")
        elif isinstance(obj, list):
            for i, item in enumerate(obj):
                check_recursive(item, f"{path}[{i}]")

    check_recursive(data, "root")

    # Group zeros by field name for cleaner output
    zero_summary = defaultdict(int)
    for p in zero_paths:
        # Extract the last part of the path (field name)
        field = p.split('.')[-1]
        # Remove list indices for grouping
        if '[' in field: 
             field = field.split('[')[0] # approximate
        else:
             parts = p.split('.')
             # Try to generalize path: matches[0].team_snapshots.home_last3_home.rank -> team_snapshots.home_last3_home.rank
             clean_path = ".".join([part.split('[')[0] for part in parts if part != "root"])
             zero_summary[clean_path] += 1

    print("\n--- Validation Report ---")
    
    if null_paths:
        print("\n[FAIL] Found Null Values:")
        for p in null_paths[:20]:
            print(f"  - {p}")
        if len(null_paths) > 20:
            print(f"  ... and {len(null_paths) - 20} more.")
    else:
        print("\n[PASS] No Null values found.")

    if zero_paths:
        print(f"\n[INFO] Found {len(zero_paths)} properties with value 0.")
        print("Summary of 0 values by field (Count):")
        sorted_zeros = sorted(zero_summary.items(), key=lambda x: x[1], reverse=True)
        for field, count in sorted_zeros:
             print(f"  - {field}: {count}")
    else:
        print("\n[PASS] No Zero values found.")

if __name__ == "__main__":
    validate_json("analysis_response_new.json")
