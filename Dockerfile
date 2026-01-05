FROM python:3.10-slim

# Install system dependencies required for LightGBM/XGBoost
RUN apt-get update && apt-get install -y --no-install-recommends \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy requirements first for cache layer
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy application code
COPY . .

# Expose port (Cloud Run defaults to 8080)
ENV PORT=8080
EXPOSE 8080

# Run the web service on container startup
CMD uvicorn src.api.main:app --host 0.0.0.0 --port ${PORT}
