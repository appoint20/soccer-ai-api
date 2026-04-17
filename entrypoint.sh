#!/bin/bash
set -e

# Start the Python AI microservice in the background
echo "Starting Soccer AI Python Microservice on port 8101..."
cd /app/ai-service
./.venv/bin/python3 -m uvicorn main:app --host 0.0.0.0 --port 8101 &
PYTHON_PID=$!

# Start the .NET API
echo "Starting Soccer AI .NET API on port $PORT..."
cd /app
dotnet soccer-ai-api.dll &
DOTNET_PID=$!

# Function to handle shutdown
cleanup() {
    echo "Shutting down services..."
    kill -TERM "$PYTHON_PID" 2>/dev/null
    kill -TERM "$DOTNET_PID" 2>/dev/null
    wait "$PYTHON_PID" "$DOTNET_PID"
    exit 0
}

trap cleanup SIGINT SIGTERM

# Simple process monitoring
while true; do
  if ! kill -0 "$PYTHON_PID" 2>/dev/null; then
    echo "Python service died. Exiting..."
    exit 1
  fi
  if ! kill -0 "$DOTNET_PID" 2>/dev/null; then
    echo "Dotnet service died. Exiting..."
    exit 1
  fi
  sleep 5
done
