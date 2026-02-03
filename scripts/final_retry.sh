#!/bin/bash

BASE_URL="http://localhost:5165/api/verify/sync/fixtures"

sync_season() {
    league_id=$1
    season=$2
    echo "Syncing League $league_id, Season $season..."
    
    response=$(curl -s -X POST "$BASE_URL/$league_id?season=$season")
    created=$(echo $response | jq '.created // 0')
    
    echo "  -> Result: Created $created"
    
    # 5s delay between seasons (internal loop has 300ms delay now)
    echo "  Waiting 5s..."
    sleep 5
}

# Serie A (135) - Missing 24, 21
sync_season 135 2024
sync_season 135 2021

# Serie B (136) - Missing 24, 23
sync_season 136 2024
sync_season 136 2023

# La Liga 2 (141) - Missing 24, 22, 21
sync_season 141 2024
sync_season 141 2022
sync_season 141 2021

# League 104 (1. Division) - specific
sync_season 104 2025
sync_season 104 2024
sync_season 104 2023

echo "Done."
