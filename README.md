# Soccer AI API

A powerful, AI-driven backend API for predicting European soccer matches, generating betting combinations, and automating ticket insights using machine learning and LLMs.

## Overview

- **Stack**: .NET 10, ASP.NET Core Web API
- **Architecture**: Domain-Driven Design (Clean Architecture)
- **Database**: SQLite (managed via EF Core Migrations)
- **AI Integration**: Google Gemini (LLM Analysis) & ML.NET (FastTree/AutoML predictions)
- **Data Source**: API-Football & internal historical logic

## Project Structure

- **`src/soccer-ai-api`**: The entry point for the REST API. Handles HTTP requests, authentication, and global exception wrapping.
- **`src/soccer-ai-application`**: The "brain" of the application. Contains all business logic (Use Cases, Commands, Queries) via Mediator, defining how match combinations are generated and analyzed.
- **`src/soccer-ai-infrastructure`**: The data layer. Handles database persistence (SQLite), external HTTP requests to API-Football, and integrations with Google Gemini.

## Requirements

You must provide the following environment variables (or secrets on Render) to run the application:
- `ApiFootball__ApiKey`: Your API-Football API key
- `Gemini__ApiKey`: Your Google Gemini API key
- `Jwt__Secret`: A secure random string for JWT token generation
- `DB_PATH`: *(Optional)* Path to the SQLite database. Defaults to `data/soccer.db`.

## Local Development

```bash
# Compile the application
dotnet build soccer-ai-api.sln

# Run the API locally
dotnet run --project src/soccer-ai-api/soccer-ai-api.csproj
```

The API will be available at `http://localhost:5000`. You can test endpoints via the provided Swagger/Scalar UI.

## Migrations (Database Updates)

The `Migrations/` folder inside the Infrastructure project contains **Entity Framework Core Migrations**. 
Migrations are simply "version history" for your database schema. Whenever you add a new property to a C# class (like adding `TeamStrength` to the `Match` model), a new Migration file is generated. When the API starts up, it reads these files to safely alter the SQLite database to match the new code without losing existing data!
