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

# Copy .NET binaries
COPY --from=dotnet-build /app/publish .

# Environment variables
ENV PORT=8080
ENV ASPNETCORE_URLS=http://+:${PORT}
ENV DB_PATH="/app/data/soccer.db"

# Create directory for SQLite
RUN mkdir -p /app/data

EXPOSE 8080
ENTRYPOINT ["dotnet", "soccer-ai-api.dll"]
