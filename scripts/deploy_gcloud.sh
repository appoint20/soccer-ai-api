#!/bin/bash
set -e

# Configuration
PROJECT_ID="soccer-gpt"
SERVICE_NAME="soccer-gpt-api"
REGION="europe-west1"

echo "=================================================="
echo "Deploying $SERVICE_NAME to Google Cloud Run"
echo "Project: $PROJECT_ID"
echo "Region:  $REGION"
echo "=================================================="

# Check for GEMINI_API_KEY
if [ -z "$GEMINI_API_KEY" ]; then
    echo "ERROR: GEMINI_API_KEY is not set in your environment."
    echo "Please run: export GEMINI_API_KEY='your_key'"
    exit 1
fi

# Deploy command
# --source . : Uploads current directory (respecting .gcloudignore) and builds via Cloud Build
# --allow-unauthenticated : Makes the API public
# --quiet : Disables interactive prompts
gcloud run deploy $SERVICE_NAME \
    --source . \
    --project $PROJECT_ID \
    --region $REGION \
    --platform managed \
    --allow-unauthenticated \
    --set-env-vars "GEMINI_API_KEY=${GEMINI_API_KEY}" \
    --memory 2Gi \
    --cpu 2 \
    --quiet

echo "=================================================="
echo "Deployment Complete!"
echo "=================================================="
