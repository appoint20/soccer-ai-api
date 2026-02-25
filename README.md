# soccer-gpt-api

## Architecture

- `src/soccer-gpt-api`: ASP.NET Core API (presentation layer).
- `src/soccer-gpt-application`: application contracts and core use-case logic.
- `src/soccer-gpt-infrastructure`: EF Core persistence, API-Football adapter, Gemini adapter, sync orchestration.
- `src/soccer-gpt-worker`: command-driven worker for Cloud Scheduler / Cloud Run Jobs.

The runtime source of truth is the SQLite database. Excel/CSV runtime ingestion has been removed.

## Security and Secrets

Do not commit secrets in `appsettings*.json`.

Required runtime variables:

- `ApiFootball__ApiKey`
- `Gemini__ApiKey`
- `AdminApi__ApiKeyHashes__0` (SHA-256 hash of a GUID key)
- `DB_PATH` (optional, default is local path)

Admin endpoints require header `X-API-Key` with a GUID value.  
The server validates it by hashing the GUID and matching against configured hashes.

Example hash generation:

```bash
echo -n "11111111-2222-3333-4444-555555555555" | shasum -a 256 | awk '{print $1}'
```

## Build and Test

```bash
dotnet build soccer-gpt-api.sln
dotnet test tests/soccer-gpt-tests/soccer-gpt-tests.csproj
```

## Worker Jobs

```bash
dotnet run --project src/soccer-gpt-worker/soccer-gpt-worker.csproj -- --job standings --season 2025
dotnet run --project src/soccer-gpt-worker/soccer-gpt-worker.csproj -- --job fixtures --season 2025
dotnet run --project src/soccer-gpt-worker/soccer-gpt-worker.csproj -- --job gemini
dotnet run --project src/soccer-gpt-worker/soccer-gpt-worker.csproj -- --job ml
dotnet run --project src/soccer-gpt-worker/soccer-gpt-worker.csproj -- --job nightly --season 2025
```

## Cloud Run Deploy (API)

Use `scripts/deploy_gcloud.sh` and set:

```bash
export GEMINI_API_KEY="..."
export APIFOOTBALL_API_KEY="..."
export ADMIN_API_KEY_HASH="..."
./scripts/deploy_gcloud.sh
```

## Notes

- Foreign key enforcement is enabled in SQLite connection string (`Foreign Keys=True`).
- Sync flow creates missing team placeholders before fixture inserts to keep FK constraints valid.
