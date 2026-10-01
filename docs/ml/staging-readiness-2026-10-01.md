# Staging readiness — 1 October 2026

## Outcome

The E2E tooling and local verification are complete. **No remote staging deployment, remote migration, production write, model promotion or live NVIDIA inference was performed.** Remote staging's URL/service has not yet been identified. The OpenRouter credential pasted into chat was not saved or used; rotate it and install the replacement securely before live testing.

| Requested step | Verified result | Remaining requirement |
|---|---|---|
| Configure credentials and accepted ML | Existing API-Football key returned HTTP 200 with active subscription; configured database has zero accepted ML generations | Valid rotated OpenRouter credential in local/staging secrets; an ML candidate that passes the acceptance gate |
| Deploy/migrate staging | Actual SQLite and disposable PostgreSQL 17 migrations and API startup passed | Exact existing remote staging service/URL, authorized deployment access and confirmation that its database is separate from production |
| Test 3–5 real upcoming matches | Three controlled fixtures passed HTTP refresh-to-database/read tests; opt-in live runner is ready | Deploy to confirmed staging, configure all sources, then select staging's own upcoming internal fixture IDs |

## Automated verification

- **812 .NET tests passed; zero failed or skipped** with a disposable localhost PostgreSQL database.
- Includes **9 HTTP E2E cases**: three successful fixture/restart cases, three missing-source/narration cases, authorization/eligibility, conflict/polling, and actual PostgreSQL migration/persistence.
- **10 staging-verifier Python tests plus 6 existing audit Python tests passed.**
- `git diff --check` passed. CI YAML parsed successfully. CI now builds the test project and starts a separate PostgreSQL regression service.
- The temporary PostgreSQL container was stopped and removed after testing.

Controlled E2E sources do **not** establish live provider compatibility, real NVIDIA output quality or improved football accuracy. Historical/ML inputs, provider predictions and narration are controlled. The numerical AI adapter/parser, production HTTP routes/authentication, background jobs, persistence, migrations and saved response retrieval are exercised.

Test logs are `/tmp/soccer-e2e-postgres-full-2026-10-01.log` and `/tmp/soccer-e2e-2026-10-01.log` on the development machine.

## Credential and data checks

API-Football's authenticated status endpoint reported an active subscription and 1,608 of 7,500 daily requests used at the check. This verifies authentication, not prediction coverage for particular fixtures.

A read-only, repeatable-read export completed at **2026-10-01 16:34 UTC** from the previously configured PostgreSQL connection. It contained 26,430 fixtures, 515 teams, 88 provider-prediction rows and **zero `GoalRateModelGenerations` rows**. This connection has not been identified as the requested remote staging environment; no deployment or migration was sent to it.

Export: `/tmp/soccer-staging-readiness-2026-10-01/`. Fixture SHA-256: `ce95dae168ae0f848d4c0ec401e733c409346961b0d9296bc8daa0d46d408d3a`.

The exported data included these potential upcoming fixtures, **not live-tested** and not guaranteed to have the same internal IDs in staging:

| Internal ID in export | Match | Kickoff UTC |
|---|---|---|
| 26847 | Eldense–Oviedo | 2026-10-02 18:30 |
| 26803 | Chesterfield–Tranmere | 2026-10-03 11:30 |
| 26840 | Albacete–Eibar | 2026-10-03 12:00 |

The local API user-secrets file still had no correctly shaped OpenRouter credential at the final check. No secret values were logged, committed or changed. OpenRouter still lists [Nemotron 3 Super's free endpoint](https://openrouter.ai/nvidia/nemotron-3-super-120b-a12b:free) with structured-output support; that listing is not proof of a successful authenticated inference request.

## Fresh ML acceptance-gate evaluation

The existing, unchanged training/evaluation pipeline ran with native **LightGBM** in a network-disabled Linux container: 25,947 labelled rows, eight chronological folds and **17,471 held-out predictions**. Fitting, calibration and evaluation blocks were disjoint. This is the broader historical acceptance-gate evaluation, **not another five-week backtest** and not a four-source comparison. Stored historical inputs were reconstructed; this does not substitute for immutable as-of input availability.

| Market | Historical Brier | Candidate hybrid Brier | Historical log-loss | Candidate hybrid log-loss |
|---|---:|---:|---:|---:|
| Over 2.5 | 0.2467 | 0.2457 | 0.686495 | 0.684504 |
| BTTS | 0.2472 | 0.2467 | 0.687601 | 0.686558 |
| Home/draw/away | 0.629979 | 0.621554 | 1.046455 | 1.034887 |
| Exactly 2–3 goals | 0.2482 | 0.2482 | **0.689479** | **0.689575** |

Lower is better. **`PublicationGatePassed=false`**: the 2–3 goals log-loss is worse than the historical baseline, so the current acceptance policy rejects promotion. The pipeline ran successfully, but successful execution is not a passed gate. The gate was not weakened and no model was published.

The table describes the existing calibrated historical/ML hybrid, not raw ML alone. Raw ML's Brier scores were **0.2493 Over 2.5** and **0.2498 BTTS**, both worse than the historical baseline. Therefore this run does not justify claiming that adding raw ML at the experimental four-source weight will improve predictions.

Full metrics: [staging ML evaluation](staging-ml-evaluation-2026-10-01.json). Local provenance: `/tmp/soccer-ml-readiness-2026-10-01/input-provenance.json`. No production database or model store was modified.

## Resume safely

Provide the remote staging URL/service identifier and configure a rotated OpenRouter credential in its secret store. Follow [the rollout and E2E runbook](../COMBINED_PREDICTIONS_E2E.md). A complete four-source live test additionally requires an accepted ML generation; otherwise the expected result is explicitly partial, not a four-source success.
