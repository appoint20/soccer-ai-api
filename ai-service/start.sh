#!/usr/bin/env bash
# Start the Soccer AI Python microservice.
# Run from the ai-service directory: ./start.sh

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VENV_DIR="$SCRIPT_DIR/.venv"
PYTHON="/opt/homebrew/bin/python3.11"

# ─── Create venv if it doesn't exist ─────────────────────────────────────────
if [ ! -d "$VENV_DIR" ]; then
    echo "→ Creating virtual environment with Python 3.11..."
    "$PYTHON" -m venv "$VENV_DIR"
fi

source "$VENV_DIR/bin/activate"

# ─── Install / upgrade dependencies ──────────────────────────────────────────
echo "→ Installing dependencies..."
pip install --upgrade pip --quiet
pip install -r "$SCRIPT_DIR/requirements.txt" --quiet

# ─── Optional: HuggingFace token (required for LLaMA 3) ──────────────────────
# export HUGGINGFACE_TOKEN="hf_your_token_here"

# ─── Model selection ─────────────────────────────────────────────────────────
export DEFAULT_MODEL="${DEFAULT_MODEL:-mistral}"

echo "→ Starting Soccer AI service on port ${PORT:-8100} (model: $DEFAULT_MODEL)"
cd "$SCRIPT_DIR"
exec "$VENV_DIR/bin/python" main.py
