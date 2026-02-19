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

# Supported Leagues
# Ligue 1
sync_season 61 2025
# Ligue 2
sync_season 62 2025
# Serie A
sync_season 135 2025
# Serie B
sync_season 136 2025
# La Liga
sync_season 140 2025
# La Liga 2
sync_season 141 2025
# Bundesliga
sync_season 78 2025
# 2. Bundesliga
sync_season 79 2025
# Premier League
sync_season 39 2025
# Championship
sync_season 40 2025
# League 104 (Cearense)
sync_season 104 2025

echo "Update Complete."
