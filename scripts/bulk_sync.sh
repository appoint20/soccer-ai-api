#!/bin/bash

# Base URL
BASE_URL="http://localhost:5165/api/verify/sync/fixtures"

# Function to sync a season
sync_season() {
    league_id=$1
    season=$2
    echo "Syncing League $league_id, Season $season..."
    
    # Run curl, capture output
    response=$(curl -s -X POST "$BASE_URL/$league_id?season=$season")
    
    # Extract 'created' count
    created=$(echo $response | jq '.created // 0')
    
    echo "  -> Result: Created $created"
}

# Ligue 1 (61)
sync_season 61 2023
sync_season 61 2022
sync_season 61 2021

# Ligue 2 (62)
sync_season 62 2023
sync_season 62 2022
sync_season 62 2021

# Serie A (135)
sync_season 135 2024
sync_season 135 2022
sync_season 135 2021

# Serie B (136)
sync_season 136 2024
sync_season 136 2023
sync_season 136 2022
sync_season 136 2021

# La Liga 2 (141)
sync_season 141 2024
sync_season 141 2023
sync_season 141 2022
sync_season 141 2021

# League 104 (Unknown/Requested)
sync_season 104 2025
sync_season 104 2024
sync_season 104 2023

echo "Done."
