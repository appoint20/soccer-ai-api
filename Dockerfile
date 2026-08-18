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

# Npgsql probes for GSSAPI/Kerberos while negotiating authentication, and the
# aspnet runtime image does not ship it:
#   Cannot load library libgssapi_krb5.so.2
# Password authentication still succeeds, so this is noise rather than a
# failure — but it is logged as "Error", which makes every real connection
# problem harder to spot in the deploy log. One small package removes it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

# Copy .NET binaries
COPY --from=dotnet-build /app/publish .

# Environment variables
ENV PORT=8080
ENV ASPNETCORE_URLS=http://+:${PORT}
ENV DB_PATH="/app/data/soccer.db"

# No file watchers on appsettings*.json: they are baked into the image and never
# change, while each watcher consumes an inotify instance from a per-UID kernel
# limit shared across the host. Exhausting it fails startup outright.
ENV DOTNET_hostBuilder__reloadConfigOnChange=false

# Create directory for SQLite
RUN mkdir -p /app/data

EXPOSE 8080
ENTRYPOINT ["dotnet", "soccer-ai-api.dll"]
