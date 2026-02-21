FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-env
WORKDIR /app

# Copy the entire solution/project structure
COPY . ./

# Restore dependencies
RUN dotnet restore src/soccer-gpt-api/soccer-gpt-api.csproj

# Build and publish a release
RUN dotnet publish src/soccer-gpt-api/soccer-gpt-api.csproj -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build-env /app/out .

# Expose port (Cloud Run defaults to 8080)
ENV PORT=8080
ENV ASPNETCORE_URLS=http://+:${PORT}
EXPOSE 8080

ENTRYPOINT ["dotnet", "soccer-gpt-api.dll"]
