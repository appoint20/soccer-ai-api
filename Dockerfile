FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .

# Explicitly exclude local DBs if any leaked in
RUN rm -f src/soccer-ai-api/data/*.db*

RUN dotnet restore src/soccer-ai-api/soccer-ai-api.csproj
RUN dotnet publish src/soccer-ai-api/soccer-ai-api.csproj -c Release -o /app/publish

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
