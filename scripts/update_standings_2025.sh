#!/bin/bash

# Base URL
BASE_URL="http://localhost:5165/api/verify/sync/standings"

# Function to sync a season
sync_standings() {
    league_id=$1
    season=$2
    echo "Syncing Standings for League $league_id, Season $season..."
    
    # Run curl, capture output
    response=$(curl -s -X POST "$BASE_URL/$league_id?season=$season")
    
    # Extract 'updated' count
    updated=$(echo $response | jq '.updated // 0')
    created=$(echo $response | jq '.created // 0')
    
    echo "  -> Result: Updated $updated, Created $created"
}

# Supported Leagues
# Ligue 1
sync_standings 61 2025
# Ligue 2
sync_standings 62 2025
# Serie A
sync_standings 135 2025
# Serie B
sync_standings 136 2025
# La Liga
sync_standings 140 2025
# La Liga 2
sync_standings 141 2025
# Bundesliga
sync_standings 78 2025
# 2. Bundesliga
sync_standings 79 2025
# Premier League
sync_standings 39 2025
# Championship
sync_standings 40 2025
# League 104 (Cearense)
sync_standings 104 2025

echo "Standings Update Complete."
