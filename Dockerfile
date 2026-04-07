FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first to cache restore layers
COPY src/soccer-ai-api/soccer-ai-api.csproj src/soccer-ai-api/
COPY src/soccer-ai-application/soccer-ai-application.csproj src/soccer-ai-application/
COPY src/soccer-ai-infrastructure/soccer-ai-infrastructure.csproj src/soccer-ai-infrastructure/

RUN dotnet restore src/soccer-ai-api/soccer-ai-api.csproj

# Now copy the rest of the source
COPY . .

# Build and publish
RUN dotnet publish src/soccer-ai-api/soccer-ai-api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app/publish .

# Setup environment for Cloud Run
ENV PORT=8080
ENV ASPNETCORE_URLS=http://+:${PORT}
ENV DB_PATH="/app/data/soccer.db"

# Create directory for SQLite
RUN mkdir -p /app/data

EXPOSE 8080
ENTRYPOINT ["dotnet", "soccer-ai-api.dll"]
