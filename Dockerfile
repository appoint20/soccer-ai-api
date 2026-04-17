# --- BUILD STAGE (.NET) ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src
COPY src/soccer-ai-api/soccer-ai-api.csproj src/soccer-ai-api/
COPY src/soccer-ai-application/soccer-ai-application.csproj src/soccer-ai-application/
COPY src/soccer-ai-infrastructure/soccer-ai-infrastructure.csproj src/soccer-ai-infrastructure/
RUN dotnet restore src/soccer-ai-api/soccer-ai-api.csproj
COPY . .
RUN dotnet publish src/soccer-ai-api/soccer-ai-api.csproj -c Release -o /app/publish --no-restore

# --- FINAL RUNTIME STAGE ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Install Python and dependencies
RUN apt-get update && apt-get install -y \
    python3 \
    python3-pip \
    python3-venv \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

# Copy .NET publish files
COPY --from=dotnet-build /app/publish .

# Copy Python AI Service
COPY ai-service/ /app/ai-service/

# Set up Python Virtual Environment
RUN python3 -m venv /app/ai-service/.venv && \
    /app/ai-service/.venv/bin/pip install --no-cache-dir -r /app/ai-service/requirements.txt

# Entrypoint script
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
