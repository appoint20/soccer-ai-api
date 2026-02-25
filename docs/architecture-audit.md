# Architecture Audit Report (Dotnet)

## Scope

Audit focus:

1. Clean structure
2. Readable/reusable code
3. Database as runtime source of truth
4. Security/secrets handling
5. Worker separation for scheduled tasks
6. FK integrity
7. Baseline tests and build stability

## What Was Incorrect

### 1) Secrets and auth

- Secrets were placed in config files and deployment was using env names not bound by .NET config hierarchy.
- Admin endpoint auth pattern was weak and not rotation-friendly.

### 2) Data source inconsistency

- Historical logic relied on file-era assumptions (Excel/CSV artifacts still present in repository and image).
- Runtime repo contained large generated data/cache artifacts (JSON, CSV, XLSX, WAL/SHM files).

### 3) Scheduling placement

- Web API and scheduler concerns were mixed together (hosted background jobs inside API project).

### 4) Data integrity

- SQLite connection explicitly disabled FK checks (`Foreign Keys=False`).

### 5) Test health

- Test project contained stale tests referencing removed services and models.

### 6) Platform consistency

- Mixed SDK target references remained (`net9.0` and nested `src/global.json`).

## Changes Applied

### Security and configuration

- Introduced hashed GUID admin key validation:
  - `AdminApi.ApiKeyHashes` with constant-time compare.
  - Backward-compatible fallback via `ADMIN_API_KEY_HASH`.
- Added env var fallback compatibility:
  - `APIFOOTBALL_API_KEY`
  - `GEMINI_API_KEY`
- Updated deploy script to pass hierarchical env vars:
  - `ApiFootball__ApiKey`
  - `Gemini__ApiKey`
  - `AdminApi__ApiKeyHashes__0`

### Database as source of truth

- Historical runtime service is DB-backed and no longer reads Excel/CSV.
- Removed ExcelDataReader dependencies and dead extension code.
- Removed tracked Excel/CSV datasets from repository.
- Removed runtime cache and output artifacts from version control.

### Worker extraction

- Added dedicated worker project: `src/soccer-gpt-worker`.
- Added command-based jobs (`nightly`, `standings`, `fixtures`, `gemini`, `ml`) for Cloud Scheduler/Cloud Run Job integration.
- Added shared orchestration service `ISyncJobRunner` + `SyncJobRunner`.
- Removed background hosted services from infrastructure registration.

### Foreign keys and sync safety

- Enabled SQLite FK enforcement (`Foreign Keys=True`).
- Added fixture-to-team FK model mapping.
- Added sync-time team placeholder creation in fixture sync to reduce FK violations.

### Tests and build

- Replaced stale tests with active tests for:
  - worker command parser
  - worker command executor
  - DB-backed historical data service
- Solution builds and tests pass on `.NET 10`.

## Current Residual Risks / Follow-up

1. Existing migration history is legacy and does not fully reflect current model shape; a clean baseline migration strategy is recommended.
2. Some non-runtime JSON assets remain in repository (`data/`, `models/` folders). If strict DB-only policy is required for all data artifacts, remove or relocate them.
3. `SyncJobRunner` currently executes ML via `python3`; worker runtime image must include Python + ML deps for `--job ml`.
