FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build-env
WORKDIR /app

# Copy the entire solution/project structure
COPY . ./

# Restore dependencies
RUN dotnet restore src/soccer-gpt-api/soccer-gpt-api.csproj

# Build and publish a release
RUN dotnet publish src/soccer-gpt-api/soccer-gpt-api.csproj -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview
WORKDIR /app
COPY --from=build-env /app/out .

# Copy ML Models to the expected absolute path: /scripts/ml/models
COPY --from=build-env /app/scripts/ml/models /scripts/ml/models

# Copy the SQLite database to the working directory
COPY --from=build-env /app/seed.db ./soccer.db

# Copy the Data folder containing historical spreadsheets/JSONs
COPY --from=build-env /app/src/soccer-gpt-infrastructure/Data /app/src/soccer-gpt-infrastructure/Data

# Expose port (Cloud Run defaults to 8080)
ENV PORT=8080
ENV ASPNETCORE_URLS=http://+:${PORT}
ENV DB_PATH="soccer.db"
EXPOSE 8080

ENTRYPOINT ["dotnet", "soccer-gpt-api.dll"]
