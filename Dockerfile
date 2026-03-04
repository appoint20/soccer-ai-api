FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .

# Explicitly exclude local DBs if any leaked in
RUN rm -f src/soccer-ai-api/data/*.db*

RUN dotnet restore src/soccer-ai-api/soccer-ai-api.csproj
RUN dotnet publish src/soccer-ai-api/soccer-ai-api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Install Python 3 and native dependencies for ML (ONNX)
RUN apt-get update && apt-get install -y \
    python3 \
    python3-pip \
    libgomp1 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# ML models and scripts
COPY --from=build /src/scripts/ml ./scripts/ml

# Install Python dependencies
# Note: Using --break-system-packages for Debian 12+ based images if not using venv
RUN pip3 install --no-cache-dir -r ./scripts/ml/requirements.txt --break-system-packages || true

# Setup environment for Cloud Run
ENV PORT=8080
ENV ASPNETCORE_URLS=http://+:${PORT}
ENV DB_PATH="/app/data/soccer.db"

# Create directory for SQLite
RUN mkdir -p /app/data

EXPOSE 8080
ENTRYPOINT ["dotnet", "soccer-ai-api.dll"]
