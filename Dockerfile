# --- STAGE 1: .NET BUILD ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src
COPY src/soccer-ai-api/soccer-ai-api.csproj src/soccer-ai-api/
COPY src/soccer-ai-application/soccer-ai-application.csproj src/soccer-ai-application/
COPY src/soccer-ai-infrastructure/soccer-ai-infrastructure.csproj src/soccer-ai-infrastructure/
RUN dotnet restore src/soccer-ai-api/soccer-ai-api.csproj
COPY . .
RUN dotnet publish src/soccer-ai-api/soccer-ai-api.csproj -c Release -o /app/publish --no-restore

# --- STAGE 2: PYTHON DEPENDENCIES ---
FROM debian:bookworm-slim AS python-deps
WORKDIR /app
RUN apt-get update && apt-get install -y \
    python3 \
    python3-pip \
    python3-venv \
    && rm -rf /var/lib/apt/lists/*

# Cache heavy libraries (PyTorch, Transformers)
COPY ai-service/requirements.txt .
RUN python3 -m venv .venv && \
    .venv/bin/pip install --no-cache-dir -r requirements.txt

# --- STAGE 3: FINAL RUNTIME ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Install Python runtime and libgomp (essential for CPU torch)
RUN apt-get update && apt-get install -y \
    python3 \
    python3-venv \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

# Copy .NET binaries from Stage 1
COPY --from=dotnet-build /app/publish .

# Copy pre-built Python environment from Stage 2
COPY --from=python-deps /app/.venv /app/ai-service/.venv/

# Copy Python source code (Last to maximize cache hits)
COPY ai-service/ /app/ai-service/

# Copy orchestration scripts
COPY entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh

# Environment variables
ENV PORT=8080
ENV ASPNETCORE_URLS=http://+:${PORT}
ENV DB_PATH="/app/data/soccer.db"

# Create directory for SQLite
RUN mkdir -p /app/data

EXPOSE 8080
ENTRYPOINT ["/app/entrypoint.sh"]
