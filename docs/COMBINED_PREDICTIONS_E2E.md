# Four-source prediction verification and staging rollout

## Two different checks

1. **Deterministic HTTP/database E2E:** `tests/soccer-ai-unit-tests/EndToEnd/CombinedPredictionEndToEndTests.cs` boots the real API with `WebApplicationFactory`, real authentication, routing, query binding, background jobs, service scopes, SQLite migrations, persistence and GET handlers. Historical/ML forecasts, API-Football and narration are controlled test inputs. The numerical AI request uses the real OpenRouter adapter/parser with a controlled HTTP response. These tests do not contact NVIDIA or measure forecasting accuracy.
2. **Opt-in live remote-staging smoke test:** `tools/e2e/verify_combined_predictions.py` calls the deployed API for 3–5 selected upcoming fixtures. It requires real credentials, an accepted model, the migrated database and fresh NVIDIA responses. It uses provider quota and appends staging predictions. It is never run automatically by CI.

## Local/CI verification

```sh
dotnet test tests/soccer-ai-unit-tests/soccer-ai-unit-tests.csproj --filter Category=EndToEnd
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests/e2e -v
```

The HTTP tests cover three fixtures, weighted 1X2 and goal markets, fresh EN/DE narration, append-only versions, raw numerical AI evidence, response persistence across host restart, missing ML, invalid AI probabilities, narration failure, stale-text suppression, admin authorization, unknown/started fixtures, polling and overlapping-job rejection. `refresh_narration=false` must still request a new numerical AI forecast, but not another narration. The earlier prediction ledger must not be overwritten.

One additional E2E test applies the PostgreSQL migrations through actual API startup and verifies a completed HTTP refresh, saved version and both language snapshots. Locally it requires `SOCCER_TEST_POSTGRES` pointing to a disposable **localhost** database named `soccer_worker_regression`; other hosts/database names are rejected. The test owns a uniquely named schema and removes that schema afterward. CI provisions a PostgreSQL 17 service automatically so this test and the existing PostgreSQL concurrency regression are not silently skipped.

The verifier's tests cover envelope parsing, incomplete jobs, bad probabilities, incorrect blending, wrong model identity, stale saved analysis, wrong whole-match selection, HTTPS/staging-host confirmation, redirect rejection and preservation of failed-job evidence. The existing CI workflow now builds the test project before `--no-build` test execution, and runs the Python checks too.

## Remote staging prerequisites

Do not use the production Render service or its database. Obtain the exact existing staging service/URL and confirm its database is separate before deployment. Nothing in the current repository identifies an existing remote staging environment.

Configure secrets through the staging platform's secret store; never place values in this document, Git, a command-line argument or chat:

- `ApiFootball__ApiKey` or `API_FOOTBALL_KEY`: valid API-Football credential.
- `OpenRouter__ApiKey` and `AiService__ApiKey`: the same valid OpenRouter credential. Alternatively use `OPENROUTER_API_KEY`, removing incorrect explicit keys first.
- `ADMIN_API_KEY`: a staging-only admin key, at least 16 characters.
- `Database__Provider=Postgres` and `ConnectionStrings__PostgresConnection`: the **staging-only** database.
- `AiStartupSync__Enabled=false`: prevents deployment from automatically requesting narration for every upcoming fixture. Ordinary startup behavior remains enabled when this setting is absent. Do not start an unrestricted worker during the bounded smoke test.
- `OpenRouter__Enabled=true`, `AiService__Enabled=true`, `HybridModel__Enabled=true`.
- `CombinedPrediction__AiModel=nvidia/nemotron-3-super-120b-a12b:free`.
- `AiService__DefaultModel=nvidia/nemotron-3-super-120b-a12b:free` and `AiService__FallbackModel=` for this **NVIDIA-only** check. A fallback response must not be misreported as NVIDIA.

Rotate any credential that was pasted into chat before deployment. The smoke runner needs only the staging admin key; upstream provider secrets belong on the server.

## Deployment and ML gates

1. Back up the staging database and deploy the current working changes using that environment's established deployment process. The existing API startup applies migrations. Confirm `20260930220000_AddCombinedPredictionSnapshotsPostgres` in staging migration history; do not apply it to an unidentified database.
2. Confirm current fixture/team/history data and at least three `NS` fixtures with kickoff safely in the future. Internal fixture IDs may differ between environments.
3. Verify an accepted generation exists in staging `GoalRateModelGenerations`, its checksums/recipe match and inference returns raw ML probabilities. Old loose model ZIP files are not an accepted generation.
4. If none exists, run the existing `train-goal-rate` command **against staging only**, on a native LightGBM-compatible runtime. Inspect `PublicationGatePassed` in its evaluation report. A successful process exit does not mean the model passed promotion. Never bypass the acceptance gate to make the four-source test green.
5. Run the bounded live smoke test. Do not deploy the experimental mixture to production on the strength of deterministic tests alone.

## Live smoke command

Set `STAGING_ADMIN_API_KEY` securely in the local process environment. Substitute the confirmed staging host and three real upcoming **internal** fixture IDs:

```sh
python3 tools/e2e/verify_combined_predictions.py \
  --base-url https://YOUR-STAGING-HOST \
  --confirm-staging-host YOUR-STAGING-HOST \
  --fixture-id 101 --fixture-id 102 --fixture-id 103 \
  --report /tmp/soccer-staging-live-report.json
```

The report records each job, source probabilities, model rationale, final markets and EN/DE narration. The program exits nonzero for partial coverage, missing narration, a non-NVIDIA model, invalid probabilities, mismatched saved analysis, failed auth or request errors. It stops after the first failed fixture rather than spending quota on the remainder. It rejects redirects and cross-origin polling URLs to protect the admin credential. A timeout may leave a server job running; inspect its saved polling URL before resubmitting. HTTPS and explicit host confirmation reduce accidental misuse but cannot prove that a URL is not production; verify its identity first.

This verifies fresh generation and saved response retrieval, not football accuracy or profitability. A separate evaluation on subsequently settled, timestamped forecasts is still required.
