#!/bin/bash
set -e

# Start the .NET API
echo "Starting Soccer AI .NET API on port $PORT..."
dotnet soccer-ai-api.dll
