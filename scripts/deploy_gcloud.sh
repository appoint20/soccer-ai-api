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

# Check required secrets
if [ -z "$GEMINI_API_KEY" ]; then
    echo "ERROR: GEMINI_API_KEY is not set in your environment."
    echo "Please run: export GEMINI_API_KEY='your_key'"
    exit 1
fi

if [ -z "$APIFOOTBALL_API_KEY" ]; then
    echo "ERROR: APIFOOTBALL_API_KEY is not set in your environment."
    echo "Please run: export APIFOOTBALL_API_KEY='your_key'"
    exit 1
fi

if [ -z "$ADMIN_API_KEY_HASH" ]; then
    echo "ERROR: ADMIN_API_KEY_HASH is not set in your environment."
    echo "Generate SHA-256 hash from your GUID admin key and export it:"
    echo "export ADMIN_API_KEY_HASH=\$(echo -n 'your-guid-key' | shasum -a 256 | awk '{print \$1}')"
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
    --set-env-vars "Gemini__ApiKey=${GEMINI_API_KEY},ApiFootball__ApiKey=${APIFOOTBALL_API_KEY},AdminApi__ApiKeyHashes__0=${ADMIN_API_KEY_HASH}" \
    --memory 2Gi \
    --cpu 2 \
    --quiet

echo "=================================================="
echo "Deployment Complete!"
echo "=================================================="
