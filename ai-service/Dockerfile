# Use a Python base image with broad support
FROM python:3.11-slim

# Set environment variables
ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1
ENV PORT=8103
ENV DEFAULT_MODEL=mistral
# Set the HF cache directory to a standard location inside the container
ENV HF_HOME=/app/model_cache

WORKDIR /app

# Install system dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential \
    curl \
    git \
    && rm -rf /var/lib/apt/lists/*

# Install Python dependencies
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy application code
COPY . .

# Argument for HuggingFace Token (required to bake Llama 3)
ARG HUGGINGFACE_TOKEN
ENV HUGGINGFACE_TOKEN=${HUGGINGFACE_TOKEN}

# Run the download script to "bake" models into the image
# This step will make the image very large (~30GB+)
RUN python download_models.py

# Expose the service port
EXPOSE 8103

# Run the service
CMD ["python", "main.py"]
