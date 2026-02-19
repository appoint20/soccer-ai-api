#!/bin/bash

# Base URL (Port 5166)
BASE_URL="http://localhost:5166/api/verify/sync/fixtures"
LEAGUES=(39 40 41 42 61 62 78 79 135 136 140 141)
SEASONS=(2025 2024 2023 2022)

echo "Starting Sync Check..."

for league in "${LEAGUES[@]}"; do
    echo "------------------------------------------------"
    echo "Processing League $league"
    
    # Test with 2025 season first
    echo "  > Testing connectivity with Season 2025..."
    response=$(curl -s -X POST "$BASE_URL/$league?season=2025")
    
    # Check if response is valid JSON and has 'created'/'updated' not null
    # A simple check is if it contains "created"
    if [[ $response == *"created"* ]]; then
        created=$(echo $response | jq '.created // 0')
        updated=$(echo $response | jq '.updated // 0')
        echo "    ✅ Success! Created: $created, Updated: $updated"
        
        # Proceed with other seasons
        for season in 2024 2023 2022; do
            echo "  > Syncing Season $season..."
            res=$(curl -s -X POST "$BASE_URL/$league?season=$season")
            c=$(echo $res | jq '.created // 0')
            u=$(echo $res | jq '.updated // 0')
            echo "    -> Created: $c, Updated: $u"
            sleep 1 # Slight delay
        done
    else
        echo "    ❌ Failed to sync 2025. Skipping remaining seasons for this league."
        echo "    Response: $response"
    fi
    
    sleep 1
done

echo "------------------------------------------------"
echo "Sync Job Completed."
