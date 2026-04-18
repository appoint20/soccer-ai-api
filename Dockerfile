# --- STAGE 1: .NET BUILD ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src
COPY src/soccer-ai-api/soccer-ai-api.csproj src/soccer-ai-api/
COPY src/soccer-ai-application/soccer-ai-application.csproj src/soccer-ai-application/
COPY src/soccer-ai-infrastructure/soccer-ai-infrastructure.csproj src/soccer-ai-infrastructure/
RUN dotnet restore src/soccer-ai-api/soccer-ai-api.csproj
COPY . .
RUN dotnet publish src/soccer-ai-api/soccer-ai-api.csproj -c Release -o /app/publish --no-restore

# --- STAGE 2: FINAL RUNTIME ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Install Python and dependencies
RUN apt-get update && apt-get install -y \
    python3 \
    python3-pip \
    python3-venv \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

# Set up Python Virtual Environment and install dependencies first (for caching)
WORKDIR /app/ai-service
COPY ai-service/requirements.txt .
RUN python3 -m venv .venv && \
    .venv/bin/pip install --no-cache-dir -r requirements.txt

# Return to root workdir
WORKDIR /app

# Copy .NET binaries
COPY --from=dotnet-build /app/publish .

# Copy Python source code
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
